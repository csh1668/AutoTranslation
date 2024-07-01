using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Diagnostics;
using UnityEngine.Networking;
using Verse;
using Verse.Noise;

namespace AutoTranslation.Translators
{
    internal class Translator_Google : ITranslator
    {
        private const string testUrl = "https://translate.google.com";
        private const string urlFormat = "https://translate.google.com/translate_a/single?client=gtx&sl={0}&tl={1}&dt=t&ie=UTF-8&oe=UTF-8&q={2}";
        private static readonly StringBuilder sb = new StringBuilder(1024);
        private string _cachedTranslateLanguage;

        public string Name  => "Google";
        private string StartLanguage => "auto";

        private string TranslateLanguage => _cachedTranslateLanguage ?? (_cachedTranslateLanguage = GetTranslateLanguage());

        public bool Ready { get; private set; }
        public bool RequiresKey => false;

        public void Prepare()
        {

            try
            {
                var resp = GetResponseUnsafe(testUrl);
                if (string.IsNullOrEmpty(resp)) throw new Exception("no response");
                Ready = true;
            }
            catch (Exception ex)
            {
                Log.Message(AutoTranslation.LogPrefix + $"Preparing Translator named '{Name}' was failed, reason: {ex.Message}");
            }
        }

        public bool TryTranslate(string text, out string translated)
        {
            try
            {
                var url = string.Format(urlFormat, StartLanguage, TranslateLanguage, UnityWebRequest.EscapeURL(text));
                translated = ParseResult(GetResponseUnsafe(url));
                return true;
            }
            catch (Exception e)
            {
                var msg = AutoTranslation.LogPrefix + $"{Name}, translate failed. reason: {e.Message}";
                Log.WarningOnce(msg + $", target: {text}", msg.GetHashCode());
                translated = text;
                return false;
            }
        }

        public static string GetResponseUnsafe(string url)
        {
            var request = WebRequest.Create(url);
            request.Method = "GET";

            using (var response = (HttpWebResponse)request.GetResponse())
            {
                if (response.StatusCode == HttpStatusCode.OK)
                {
                    using (var stream = response.GetResponseStream())
                    {
                        using (var reader = new StreamReader(stream))
                        {
                            return reader.ReadToEnd();
                        }
                    }
                }
                else
                {
                    throw new Exception($"Request failed with status: {response.StatusCode}");
                }
            }
        }

        private static string ParseResult(string text)
        {
            sb.Clear();
            var flag = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '"' && i > 0 && text[i - 1] != '\\')
                {
                    if (flag)
                        break;
                    flag = true;
                }
                else if (flag) sb.Append(text[i]);
            }
            return sb.ToString();
        }

        private static string GetTranslateLanguage()
        {
            if (LanguageDatabase.activeLanguage == null)
            {
                Log.Warning(AutoTranslation.LogPrefix + "activeLanguage was null");
                return "en";
            }

            switch (LanguageDatabase.activeLanguage.LegacyFolderName)
            {
                case "Korean": return "ko";
                case "Catalan": return "ca";
                case "ChineseSimplified": return "zh-CN";
                case "ChineseTraditional": return "zh-TW";
                case "Czech": return "cs";
                case "Danish": return "da";
                case "Dutch": return "nl";
                case "Estonian": return "et";
                case "Finnish": return "fi";
                case "French": return "fr";
                case "German": return "de";
                case "Greek": return "el";
                case "Hungarian": return "hu";
                case "Italian": return "it";
                case "Japanese": return "ja";
                case "Norwegian": return "no";
                case "Polish": return "pl";
                case "Portuguese": return "pt-PT";
                case "PortugueseBrazilian": return "pt";
                case "Romanian": return "ro";
                case "Russian": return "ru";
                case "Slovak": return "sk";
                case "SpanishLatin":
                case "Spanish": return "es";
                case "Swedish": return "sv";
                case "Turkish": return "tr";
                case "Ukrainian": return "uk";
                case "English": return "en";
                case "Vietnamese": return "vi";
                default:
                    Log.Error(AutoTranslation.LogPrefix +
                                $"Unsupported language: {LanguageDatabase.activeLanguage.LegacyFolderName}");
                    return "en";
            }
        }
    }
}
