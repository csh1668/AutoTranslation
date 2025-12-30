using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AutoTranslation.Translators;
using RimWorld;
using UnityEngine.Networking;
using Verse;

namespace AutoTranslation
{
    public static class TranslatorManager
    {
        public static readonly ConcurrentDictionary<string, string> CachedTranslations = new ConcurrentDictionary<string, string>();
        public static ITranslator CurrentTranslator;
        public static bool Ready;
        public static bool Working;

        // <(original, normalized), callback>
        internal static readonly ConcurrentQueue<KeyValuePair<string, Action<string, bool>>> _queue = new ConcurrentQueue<KeyValuePair<string, Action<string, bool>>>();
        internal static readonly List<ITranslator> translators = new List<ITranslator>();
        internal static int workCnt;
        internal static int _cacheCount;

        private static readonly ConcurrentDictionary<string, byte> _inQueue = new ConcurrentDictionary<string, byte>();
        private static readonly ConcurrentDictionary<string, int> _retryCount = new ConcurrentDictionary<string, int>();
        private const int MAX_RETRIES = 3; // Maximum number of times to retry a failed translation
        private static Task _translationThread;
        private static Timer _cacheSaver;
        private static SemaphoreSlim _concurrencyLimiter;
        private static int _currentMaxConcurrency;

        public static void Prepare()
        {
            translators.Clear();
            var translatorTypes = GenTypes.AllTypes
                .Where(x => !x.IsAbstract && !x.IsInterface &&  x.GetInterface(nameof(ITranslator)) != null).ToList();
            
            foreach (var translatorType in translatorTypes)
            {
                var t = (ITranslator)Activator.CreateInstance(translatorType);
                
                // Inject settings if available
                if (Settings.TranslatorSettings.TryGetValue(t.Name, out var savedSettings))
                {
                    t.Settings = savedSettings;
                }
                
                t.Prepare();
                
                // If the translator created default settings, save them back
                if (t.Settings != null)
                {
                    Settings.TranslatorSettings[t.Name] = t.Settings;
                }

                translators.Add(t);
            }
            
            CurrentTranslator = GetTranslator(Settings.TranslatorName);
            if (CurrentTranslator?.Ready == false) CurrentTranslator = null;

            if (CurrentTranslator == null)
            {
                CurrentTranslator = translators.FirstOrDefault(x => x.Ready);
                if (CurrentTranslator != null)
                {
                     Log.Error(AutoTranslation.LogPrefix +
                            $"Selected translator named {Settings.TranslatorName} is not ready, changing to other translator.. {CurrentTranslator?.Name}");
                     Settings.TranslatorName = CurrentTranslator.Name;
                }
            }
            Log.Message(AutoTranslation.LogPrefix + $"List of translators: {translators.Select(x => x.Name).ToCommaList()}, Current translator: {CurrentTranslator?.Name}");
            Ready = CurrentTranslator != null;

            // Load from new cache manager (V2 format)
            TranslationCacheManager.Load("CachedTranslationsV2");
            foreach (var pair in TranslationCacheManager.GetAll())
            {
                CachedTranslations[pair.Key] = pair.Value;
            }

            _cacheCount = CachedTranslations.Count;

        }

        /// <summary>
        /// Creates a cache key from the original text and optional additional key.
        /// Since we now use Base64 encoding for XML storage, we can keep the original text intact.
        /// </summary>
        private static string NormalizeKey(string text)
        {
            if (text.NullOrEmpty())
            {
                return text;
            }

            // Simple normalization: trim whitespace and normalize line endings
            // Keep the original text as much as possible for better debugging
            text = text.Trim();
            text = text.Replace("\r\n", "\n");  // Normalize Windows line endings
            text = text.Replace('\r', '\n');    // Normalize old Mac line endings
            
            // No need to append length - the full text is already unique
            return text;
        }

        internal static string PolishText(string text)
        {
            return Regex.Unescape(Regex.Replace(text, "\\[Uu]([0-9A-Fa-f]{4})",
                    m => char.ToString((char)ushort.Parse(m.Groups[1].Value, NumberStyles.AllowHexSpecifier)))
                .Replace("\\\"", "\"")).Trim();
        }

        public static void StartThread()
        {
            if (CurrentTranslator == null)
            {
                Log.Error(AutoTranslation.LogPrefix + $"::Critical Error:: CurrentTranslator was null. Couldn't get any available Translator within {translators.Select(x => x.Name).ToCommaList()}");
                return;
            }

            Working = true;
            _concurrencyLimiter = new SemaphoreSlim(Settings.MaxConcurrency);
            _currentMaxConcurrency = Settings.MaxConcurrency;

            _translationThread = Task.Factory.StartNew(async () =>
            {
                while (true)
                {
                    if (!Working)
                    {
                        await Task.Delay(1000);
                        continue;
                    }

                    // Dynamically update concurrency limit if settings changed
                    if (_currentMaxConcurrency != Settings.MaxConcurrency)
                    {
                        _concurrencyLimiter?.Dispose();
                        _concurrencyLimiter = new SemaphoreSlim(Settings.MaxConcurrency);
                        _currentMaxConcurrency = Settings.MaxConcurrency;
                        Log.Message(AutoTranslation.LogPrefix + $"Updated concurrency limit to {Settings.MaxConcurrency}");
                    }
                    
                    if (_queue.Count > 0)
                    {
                        await _concurrencyLimiter.WaitAsync();
                        
                        if (_queue.TryDequeue(out var pair))
                        {
                            _inQueue.TryRemove(pair.Key, out _);
                            
                            _ = Task.Run(() =>
                            {
                                try
                                {
                                    var translated = string.Empty;
                                    var success = true;
                                    
                                    // Batch/Tokenize logic
                                    if (pair.Key.Length > 200)
                                    {
                                        translated = pair.Key.Tokenize().Aggregate(translated, (current, token) =>
                                        {
                                            success &= CurrentTranslator.TryTranslate(token, out var tmp);
                                            return current + ' ' + tmp;
                                        });
                                    }
                                    else
                                    {
                                        success = CurrentTranslator.TryTranslate(pair.Key, out translated);
                                    }

                                    if (success)
                                    {
                                        translated = PolishText(translated);
                                        // Success - clear retry count
                                        _retryCount.TryRemove(pair.Key, out _);
                                    }
                                    Interlocked.Increment(ref workCnt);
                                    pair.Value(translated, success);
                                }
                                catch (Exception ex)
                                {
                                    // Check if this is a network timeout/error that we can retry
                                    bool shouldRetry = ex is WebException webEx && 
                                        (webEx.Status == WebExceptionStatus.Timeout || 
                                         webEx.Status == WebExceptionStatus.ConnectionClosed ||
                                         webEx.Status == WebExceptionStatus.ReceiveFailure);
                                    
                                    if (shouldRetry)
                                    {
                                        var currentRetries = _retryCount.AddOrUpdate(pair.Key, 1, (k, v) => v + 1);
                                        
                                        if (currentRetries <= MAX_RETRIES)
                                        {
                                            // Re-add to queue for retry
                                            Log.Warning($"{AutoTranslation.LogPrefix} Translation failed (timeout/network error), adding back to queue (retry {currentRetries}/{MAX_RETRIES}): {pair.Key.Substring(0, Math.Min(50, pair.Key.Length))}...");
                                            _queue.Enqueue(pair);
                                            _inQueue.TryAdd(pair.Key, 0);
                                        }
                                        else
                                        {
                                            // Max retries exceeded
                                            Log.Error($"{AutoTranslation.LogPrefix} Translation failed after {MAX_RETRIES} retries: {ex.Message}");
                                            _retryCount.TryRemove(pair.Key, out _);
                                            pair.Value(pair.Key, false);
                                        }
                                    }
                                    else
                                    {
                                        // Non-retryable error
                                        Log.Error($"{AutoTranslation.LogPrefix} Translation failed with non-retryable error: {ex.Message}");
                                        _retryCount.TryRemove(pair.Key, out _);
                                        pair.Value(pair.Key, false);
                                    }
                                }
                                finally
                                {
                                    _concurrencyLimiter.Release();
                                }
                            });
                        }
                        else
                        {
                            _concurrencyLimiter.Release();
                        }
                    }
                    else
                    {
                        await Task.Delay(500);
                    }
                }
            }, TaskCreationOptions.LongRunning).Unwrap().ContinueWith(t =>
            {
                Log.Warning($"Translation thread was killed! {t.Exception?.Message}");
            });

            _cacheSaver = new Timer(state =>
            {
                if (!Working) return;
                try
                {
                    // Sync local cache to manager
                    foreach (var pair in CachedTranslations)
                    {
                        TranslationCacheManager.AddOrUpdate(pair.Key, pair.Value);
                    }

                    if (TranslationCacheManager.IsDirty || _cacheCount != CachedTranslations.Count)
                    {
                        TranslationCacheManager.Save("CachedTranslationsV2");
                        
                        if (_cacheCount != CachedTranslations.Count)
                        {
                            Log.Message(AutoTranslation.LogPrefix +
                                        $"Translation cache saved to your disk. translated: {CachedTranslations.Count}");
                            _cacheCount = CachedTranslations.Count;
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.Message($"ERROR: {e.Message}");
                }
            }, null, 0, 60000);
        }

        public static void Translate(string orig, Action<string> callBack) => Translate(orig, string.Empty, string.Empty, callBack);

        public static void Translate(string orig, string additionalKey, Action<string> callBack) => Translate(orig, additionalKey, string.Empty, callBack);

        public static void Translate(string orig, string additionalKey, string modPackageId, Action<string> callBack)
        {
            if (string.IsNullOrEmpty(orig))
            {
                callBack(orig);
                return;
            }
            
            // Include ModPackageId in the key for proper grouping
            var keyPrefix = string.IsNullOrEmpty(modPackageId) ? "" : $"{modPackageId}:";
            var normalizedText = NormalizeKey(orig + additionalKey);
            var key = keyPrefix + normalizedText;
            
            // Check cache first (local dict is synced on load)
            if (CachedTranslations.TryGetValue(key, out var translation))
            {
                callBack(Prefs.DevMode && Settings.AppendTranslationCompleteTag && orig != translation ? "::TEST::" + translation : translation);
                return;
            }
            
            // Backward compatibility: check for old cache entries without ModId
            // If found, return original text to trigger re-translation with proper ModId
            if (!string.IsNullOrEmpty(keyPrefix) && CachedTranslations.ContainsKey(normalizedText))
            {
                // Old entry exists - will be cleaned up on next save
                // Don't use it, let it re-translate with ModId
            }

            if (!Ready)
            {
                callBack(orig);
                return;
            }

            if (_inQueue.ContainsKey(key))
                return;
            _inQueue[key] = 0; // dummy value
            _queue.Enqueue(new KeyValuePair<string, Action<string, bool>>(orig, (t, s) =>
            {
                if (s)
                {
                    CachedTranslations[key] = t;
                    // Also update manager immediately? Or wait for timer?
                    // Timer syncs every minute, might be safer to update immediately for robustness
                    TranslationCacheManager.AddOrUpdate(key, t);
                }
                callBack(s && Prefs.DevMode && Settings.AppendTranslationCompleteTag && orig != t ? "::TEST::" + t : t);
            }));
        }

        public static ITranslator GetTranslator(string name)
        {
            return translators.FirstOrDefault(x => x.Name == name);
        }

        public static void ClearQueue()
        {
            Working = false;
            workCnt = 0;

            while (_queue.Count > 0)
            {
                _queue.TryDequeue(out var pair);
            }
            _inQueue.Clear();

            Working = true;
        }
    }
}
