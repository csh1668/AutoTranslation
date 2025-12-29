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
        private static Task _translationThread;
        private static Task[] _translationThreads;
        private static Timer _cacheSaver;
        private static readonly Regex StringFormatSymbolsRegex = new Regex("{.*?}");
        private static readonly StringBuilder sb = new StringBuilder(1024);
        private static readonly object lockObj = new object();
        //private static readonly Queue<KeyValuePair<string, Action<string, bool>>> _finished = new Queue<KeyValuePair<string, Action<string, bool>>>();

        public static void Prepare()
        {
            var translatorTypes = GenTypes.AllTypes
                .Where(x => !x.IsAbstract && !x.IsInterface &&  x.GetInterface(nameof(ITranslator)) != null).ToList();
            foreach (var translatorType in translatorTypes)
            {
                var t = (ITranslator)Activator.CreateInstance(translatorType);
                translators.Add(t);
            }
            CurrentTranslator = GetTranslator(Settings.TranslatorName);

            if (CurrentTranslator == null)
            {
                CurrentTranslator = translators.FirstOrDefault(x => x.Name == "Google");
                Log.Error(AutoTranslation.LogPrefix +
                            $"Selected translator named {Settings.TranslatorName} is not ready, changing to other translator.. {CurrentTranslator?.Name}");
            }
            Log.Message(AutoTranslation.LogPrefix + $"List of translators: {translators.Select(x => x.Name).ToCommaList()}, Current translator: {CurrentTranslator?.Name}");
            Ready = CurrentTranslator != null;

            foreach (var (k, v) in CacheFileTool.Import(nameof(CachedTranslations)))
            {
                CachedTranslations[k] = v;
            }

            _cacheCount = CachedTranslations.Count;

        }

        //public static string TranslateSync(string orig)
        //{
        //    if (CurrentTranslator == null)
        //    {
        //        var err = AutoTranslation.LogPrefix + "CurrentTranslator was null.";
        //        Log.ErrorOnce(err, err.GetHashCode());
        //        return orig;
        //    }

        //    var key = NormalizeKey(orig);
        //    if (!CachedTranslations.TryGetValue(key, out var translated))
        //    {
        //        translated = CurrentTranslator.Translate(orig);
        //        if (orig.GetHashCode() != translated.GetHashCode())
        //            CachedTranslations[key] = translated;
        //    }
        //    return Settings.AppendTranslationCompleteTag ? "::TEST::" + translated : translated;
        //}

        private static string NormalizeKey(string handle)
        {
            var len = handle.Length;
            if (handle.NullOrEmpty())
            {
                return handle;
            }
            handle = handle.Trim();
            handle = handle.Replace(' ', '_');
            handle = handle.Replace('\n', '_');
            handle = handle.Replace("\r", "");
            handle = handle.Replace('\t', '_');
            handle = handle.Replace(".", "");
            handle = Regex.Replace(handle, @"[^\p{L}\p{Nl}a-zA-Z0-9_\.]", "");
            sb.Length = 0;
            sb.Append(handle);
            sb.Append(len);
            handle = sb.ToString();
            handle = handle.Trim('_');
            handle = handle.Trim('-'); // for HSK
            if (!handle.NullOrEmpty() && char.IsDigit(handle[0]))
            {
                handle = "_" + handle;
            }
            return handle;
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

            _translationThreads = new Task[Settings.ConcurrentWorkerCount];
            for (int i = 0; i < Settings.ConcurrentWorkerCount; i++)
            {
                _translationThreads[i] = Task.Factory.StartNew(DoInnerThreadWork).ContinueWith(t =>
                {
                    Log.Warning($"Translation thread {i} was killed! {t.Exception?.Message}");
                });
            }

            _cacheSaver = new Timer(state =>
            {
                if (!Working) return;
                try
                {
                    CacheFileTool.Export(nameof(CachedTranslations), new Dictionary<string, string>(CachedTranslations));
                    if (_cacheCount != CachedTranslations.Count)
                    {
                        Log.Message(AutoTranslation.LogPrefix +
                                    $"Translation cache saved to your disk. translated: {CachedTranslations.Count}");
                        _cacheCount = CachedTranslations.Count;
                    }
                }
                catch (Exception e)
                {
                    Log.Message($"ERROR: {e.Message}");
                }
            }, null, 0, 60000);
        }

        private static void DoInnerThreadWork()
        {
            while (true)
            {
                if (!Working)
                {
                    Thread.Sleep(1000);
                    continue;
                }
                while (_queue.Count > 0)
                {
                    if (Settings.SleepTime > 0) Thread.Sleep(Settings.SleepTime);

                    if (!_queue.TryDequeue(out var pair)) continue;

                    _inQueue.TryRemove(pair.Key, out _);
                    var translated = string.Empty;
                    var success = true;
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
                        //translated = UnityWebRequest.UnEscapeURL(translated, Encoding.UTF8).Trim();
                    }
                    workCnt++;
                    //_finished.Enqueue(pair);
                    pair.Value(translated, success);
                }
                Thread.Sleep(1000);
            }
        }

        public static void Translate(string orig, Action<string> callBack) => Translate(orig, string.Empty, callBack);

        public static void Translate(string orig, string additionalKey, Action<string> callBack)
        {
            if (string.IsNullOrEmpty(orig))
            {
                callBack(orig);
                return;
            }
            var key = NormalizeKey(orig + additionalKey);
            if (CachedTranslations.TryGetValue(key, out var translation))
            {
                callBack(Prefs.DevMode && Settings.AppendTranslationCompleteTag && orig != translation ? "::TEST::" + translation : translation);
                return;
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
                //_finished.Enqueue(pair);
            }
            _inQueue.Clear();
            //foreach (var pair in _finished)
            //{
            //    _queue.Enqueue(pair);
            //}

            //_finished.Clear();

            Working = true;
        }
    }
}
