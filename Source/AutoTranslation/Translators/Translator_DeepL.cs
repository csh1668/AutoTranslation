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
    internal class Translator_DeepL : ITranslator
    {
        private static readonly StringBuilder sb = new StringBuilder(1024);
        private string _cachedTranslateLanguage;
        protected virtual string url => $"https://api-free.deepl.com/v2/translate";

        public virtual string Name => "DeepL";
        public bool Ready { get; private set; }
        public bool RequiresKey => true;

        protected string TranslateLanguage => _cachedTranslateLanguage ?? (_cachedTranslateLanguage = GetTranslateLanguage());

        public void Prepare()
        {
            if (string.IsNullOrEmpty(Settings.APIKey))
                return;
            Ready = true;
        }

        public bool TryTranslate(string text, out string translated)
        {
            if (string.IsNullOrEmpty(text))
            {
                translated = string.Empty;
                return true;
            }
            try
            {
                translated = Parse(GetResponseUnsafe(url, new List<IMultipartFormSection>()
                {
                    new MultipartFormDataSection("auth_key", Settings.APIKey),
                    new MultipartFormDataSection("text", EscapePlaceholders(text)),
                    //new MultipartFormDataSection("source_lang", "EN"),
                    new MultipartFormDataSection("target_lang", TranslateLanguage),
                    new MultipartFormDataSection("preserve_formatting", "true"),
                    new MultipartFormDataSection("tag_handling", "xml"),
                    new MultipartFormDataSection("ignore_tags", "x")
                }), out var detectedLang);
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


        public static string GetResponseUnsafe(string url, List<IMultipartFormSection> form)
        {
            var request = UnityWebRequest.Post(url, form);

            var asyncOperation = request.SendWebRequest();
            while (!asyncOperation.isDone)
            {
                Thread.Sleep(1);
            }

            if (request.isNetworkError || request.isHttpError)
            {
                throw new Exception($"Web error: {request.error}");
            }

            return request.downloadHandler.text;
        }
        public static string Parse(string text, out string detectedLang)
        {
            const string detectKey = "\"detected_source_language\":\"";
            var startIdx = text.IndexOf(detectKey, StringComparison.Ordinal) + detectKey.Length;
            var endIdx = startIdx + 1;
            for (; endIdx < text.Length; endIdx++) if (text[endIdx] == '\"') break;
            detectedLang = text.Substring(startIdx, endIdx - startIdx);

            const string textKey = "\"text\":\"";
            startIdx = text.IndexOf(textKey, StringComparison.Ordinal) + textKey.Length;
            endIdx = text.LastIndexOf('\"');

            return text.Substring(startIdx, endIdx - startIdx);
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
            if (LanguageDatabase.activeLanguage == null)
            {
                Log.Warning(AutoTranslation.LogPrefix + "activeLanguage was null");
                return "EN";
            }

            switch (LanguageDatabase.activeLanguage.LegacyFolderName)
            {
                case "Korean": return "KO";
                //case "Catalan": return "ca";
                case "ChineseSimplified": return "ZH";
                //case "Traditional Chinese": return "zh-TW";
                case "Czech": return "CS";
                case "Danish": return "DA";
                case "Dutch": return "NL";
                case "Estonian": return "ET";
                case "Finnish": return "FI";
                case "French": return "FR";
                case "German": return "DE";
                case "Greek": return "EL";
                case "Hungarian": return "HU";
                case "Italian": return "IT";
                case "Japanese": return "JA";
                case "Norwegian": return "NB";
                case "Polish": return "PL";
                case "Portuguese": return "PT-PT";
                case "PortugueseBrazilian": return "PT-BR";
                case "Romanian": return "RO";
                case "Russian": return "RU";
                case "Slovak": return "SK";
                case "SpanishLatin":
                case "Spanish": return "ES";
                case "Swedish": return "SV";
                case "Turkish": return "TR";
                case "Ukrainian": return "UK";
                case "English": return "EN";
                default:
                    Log.Error(AutoTranslation.LogPrefix +
                                $"Unsupported language: {LanguageDatabase.activeLanguage.FriendlyNameEnglish}, Please change the Translator.");
                    return "EN";
            }
        }
    }
}
