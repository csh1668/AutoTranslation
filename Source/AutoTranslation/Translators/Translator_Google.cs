using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
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
                var t = ParseResult(GetResponseUnsafe(url), out var detectedLang);
                translated = detectedLang == TranslateLanguage ? text : t;
                return true;
            }
            catch (Exception e)
            {
                var msg = AutoTranslation.LogPrefix + $"{Name}, translate failed. reason: {e.GetType()}|{e.Message}";
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

        internal static string ParseResult(string text, out string detectedLang)
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

            var pattern = @"\[""([^""]+)""\]\]\]";
            var match = Regex.Match(text, pattern);
            detectedLang = "aaaaa"; /*match.Success ? match.Groups[1].Value : string.Empty;*/

            return sb.ToString();
        }


        private static readonly Dictionary<string, string> TranslateLanguageGetter = new Dictionary<string, string>
        {
            ["Korean"] = "ko",
            ["Catalan"] = "ca",
            ["ChineseSimplified"] = "zh-CN",
            ["ChineseTraditional"] = "zh-TW",
            ["Czech"] = "cs",
            ["Danish"] = "da",
            ["Dutch"] = "nl",
            ["Estonian"] = "et",
            ["Finnish"] = "fi",
            ["French"] = "fr",
            ["German"] = "de",
            ["Greek"] = "el",
            ["Hungarian"] = "hu",
            ["Italian"] = "it",
            ["Japanese"] = "ja",
            ["Norwegian"] = "no",
            ["Polish"] = "pl",
            ["Portuguese"] = "pt-PT",
            ["PortugueseBrazilian"] = "pt",
            ["Romanian"] = "ro",
            ["Russian"] = "ru",
            ["Slovak"] = "sk",
            ["SpanishLatin"] = "es",
            ["Spanish"] = "es",
            ["Swedish"] = "sv",
            ["Turkish"] = "tr",
            ["Ukrainian"] = "uk",
            ["English"] = "en",
            ["Vietnamese"] = "vi",
            ["Thai"] = "th"
        };
        private static string GetTranslateLanguage()
        {
            if (LanguageDatabase.activeLanguage == null)
            {
                Log.Warning(AutoTranslation.LogPrefix + "activeLanguage was null");
                return "en";
            }

            if (!TranslateLanguageGetter.TryGetValue(LanguageDatabase.activeLanguage.LegacyFolderName, out var res))
            {
                Log.Error(AutoTranslation.LogPrefix + $"Unsupported language: {LanguageDatabase.activeLanguage.LegacyFolderName}");
                res = "en";
            }

            return res;
        }
    }
}
