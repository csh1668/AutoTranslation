using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Diagnostics;
using UnityEngine.Networking;
using Verse;

namespace AutoTranslation.Translators
{
    public class Translator_DeepL : Translator_BaseTraditional
    {
        private static readonly StringBuilder sb = new StringBuilder(1024);
        private string _cachedTranslateLanguage;
        protected virtual string url => $"https://api-free.deepl.com/v2/translate";

        public override string Name => "DeepL";
        public override bool RequiresKey => true;
        public override string TranslateLanguage => _cachedTranslateLanguage ?? (_cachedTranslateLanguage = GetTranslateLanguage());


        public override bool TryTranslate(string text, out string translated)
        {
            return TryTranslate(text, out translated, false);
        }

        public override bool TryTranslate(string text, out string translated, bool skipRetry)
        {
            if (string.IsNullOrEmpty(text))
            {
                translated = string.Empty;
                return true;
            }
            try
            {
                translated = Parse(GetResponseUnsafe(url, APIKey, $@"
                    {{

                        ""text"": [""{EscapePlaceholders(text)}""],
                        ""target_lang"": ""{TranslateLanguage}"",
                        ""preserve_formatting"": true,
                        ""tag_handling"": ""xml"",
                        ""ignore_tags"": [""x""]
                    }}
                ", skipRetry), out var detectedLang);
                translated = detectedLang == TranslateLanguage ? text : UnEscapePlaceholders(translated);

                return true;
            }
            catch (Exception e)
            {
                var msg = AutoTranslation.LogPrefix + $"{Name}, translate failed. reason: {e.GetType()}|{e.Message}";
                Log.WarningOnce(msg, msg.GetHashCode());
            }

            translated = text;
            return false;
        }

        public override bool SupportsCurrentLanguage()
        {
            var lang = LanguageDatabase.activeLanguage?.LegacyFolderName;
            if (lang == null)
            {
                Log.Warning(AutoTranslation.LogPrefix + "activeLanguage was null");
                return false;
            }

            return _languageMap.ContainsKey(lang);
        }

        protected string APIKey =>
            rotater == null ? (rotater = new APIKeyRotater(Settings.APIKey.Split(','))).Key : rotater.Key;

        protected APIKeyRotater rotater = null;

        public static string GetResponseUnsafe(string url, string apiKey, string body, bool skipRetry = false)
        {
            const int maxRetries = 5;
            const int initialDelayMs = 1000; // 1 second
            const int maxDelayMs = 60000; // 60 seconds

            var rand = new Random();
            var retryCount = 0;

            while (true)
            {
                var request = new UnityWebRequest(url, "POST");
                byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();

                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Authorization", $"DeepL-Auth-Key {apiKey}");

                try
                {
                    var asyncOperation = request.SendWebRequest();
                    while (!asyncOperation.isDone)
                    {
                        Thread.Sleep(1);
                    }

                    // Check for HTTP 429 specifically
                    if (request.responseCode == 429)
                    {
                        // If skipRetry is true, don't attempt to retry
                        if (skipRetry)
                        {
                            Log.Warning(AutoTranslation.LogPrefix + "DeepL API rate limit exceeded (HTTP 429). Skip retry flag is set, failing immediately.");
                            throw new Exception($"Web error: Rate limit exceeded (HTTP 429) - retry skipped");
                        }
                        
                        // Too many requests - handle with exponential backoff
                        if (retryCount >= maxRetries)
                        {
                            Log.Error(AutoTranslation.LogPrefix + $"DeepL API rate limit exceeded. Maximum retries ({maxRetries}) reached.");
                            throw new Exception($"Web error: Rate limit exceeded (HTTP 429) - maximum retries reached");
                        }

                        retryCount++;

                        // Calculate delay with exponential backoff and jitter
                        int delayMs = (int)Math.Min(maxDelayMs, initialDelayMs * Math.Pow(2, retryCount - 1));
                        // Add jitter (±20% randomness) to avoid thundering herd problem
                        delayMs = (int)(delayMs * (0.8 + 0.4 * rand.NextDouble()));

                        Log.Warning(AutoTranslation.LogPrefix + $"DeepL API rate limit exceeded (HTTP 429). Retrying in {delayMs / 1000.0:F1} seconds (attempt {retryCount}/{maxRetries})");

                        // Dispose of the current request before sleeping
                        request.Dispose();

                        Thread.Sleep(delayMs);
                        continue; // Retry the request
                    }
                    else if (request.isNetworkError || request.isHttpError)
                    {
                        throw new Exception($"Web error: {request.error}");
                    }

                    // Success - return the response
                    return request.downloadHandler.text;
                }
                catch (Exception ex)
                {
                    // For other exceptions that aren't HTTP 429, propagate them up
                    if (request.responseCode != 429)
                    {
                        throw;
                    }

                    // If the exception was already handled in the HTTP 429 block, 
                    // we shouldn't reach here, but just in case
                    throw new Exception($"Error during DeepL API request: {ex.Message}", ex);
                }
                finally
                {
                    // Ensure request is properly disposed
                    request.Dispose();
                }
            }
        }

        public static string Parse(string text, out string detectedLang)
        {
            detectedLang = text.GetStringValueFromJson("detected_source_language");
            return text.GetStringValueFromJson("text");
        }

        public static string EscapePlaceholders(string text)
        {
            return Regex.Replace(text, @"[\{](.*?)[\}]", match => $"<x>{match.Value}</x>");
        }

        public static string UnEscapePlaceholders(string text)
        {
            return text.Replace("<x>{", "{").Replace("}</x>", "}");
        }

        private static string GetTranslateLanguage()
        {
            var lang = LanguageDatabase.activeLanguage?.LegacyFolderName;
            if (lang == null)
            {
                Log.Warning(AutoTranslation.LogPrefix + "activeLanguage was null");
                return "EN";
            }

            lang = lang.Split('_').First();
            if (_languageMap.TryGetValue(lang, out var result))
                return result;

            Log.Error(AutoTranslation.LogPrefix + $"Unsupported language: {lang} in DeepL, Please change to another translator.");
            return "EN";
        }

        private static readonly Dictionary<string, string> _languageMap = new Dictionary<string, string>
        {
            ["Korean"] = "KO",
            ["ChineseSimplified"] = "ZH",
            ["Czech"] = "CS",
            ["Danish"] = "DA",
            ["Dutch"] = "NL",
            ["Estonian"] = "ET",
            ["Finnish"] = "FI",
            ["French"] = "FR",
            ["German"] = "DE",
            ["Greek"] = "EL",
            ["Hungarian"] = "HU",
            ["Italian"] = "IT",
            ["Japanese"] = "JA",
            ["Norwegian"] = "NB",
            ["Polish"] = "PL",
            ["Portuguese"] = "PT-PT",
            ["PortugueseBrazilian"] = "PT-BR",
            ["Romanian"] = "RO",
            ["Russian"] = "RU",
            ["Slovak"] = "SK",
            ["SpanishLatin"] = "ES",
            ["Spanish"] = "ES",
            ["Swedish"] = "SV",
            ["Turkish"] = "TR",
            ["Ukrainian"] = "UK",
            ["English"] = "EN"
        };
    }
}
