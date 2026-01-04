using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine.Networking;
using Verse;
using AutoTranslation;
using AutoTranslation.Utilities;

namespace AutoTranslation.Translators
{
    public class Translator_Google : Translator_BaseTraditional
    {
        private const string testUrl = "https://translate.google.com";
        private const string urlFormat = "https://translate.google.com/translate_a/single?client=gtx&sl={0}&tl={1}&dt=t&ie=UTF-8&oe=UTF-8&q={2}";
        private static readonly StringBuilder sb = new StringBuilder(1024);
        private string _cachedTranslateLanguage;

        public override string Name  => "Google";

        public override string TranslateLanguage => _cachedTranslateLanguage ?? (_cachedTranslateLanguage = GetTranslateLanguage());
        public override bool RequiresKey => false;

        public override void Prepare()
        {
            try
            {
                var resp = NetworkHelper.Get(testUrl);
                if (string.IsNullOrEmpty(resp)) throw new Exception("no response");
                Ready = true;
            }
            catch (Exception ex)
            {
                Log.Message(AutoTranslation.LogPrefix + $"Preparing Translator named '{Name}' was failed, reason: {ex.Message}");
            }
        }

        public override bool TryTranslate(string text, out string translated)
        {
            try
            {
                // Protect placeholders
                var (protectedText, placeholders) = text.ProtectPlaceholders();
                
                var url = string.Format(urlFormat, StartLanguage, TranslateLanguage, UnityWebRequest.EscapeURL(protectedText));
                var t = ParseResult(NetworkHelper.Get(url), out var detectedLang);
                
                // Restore placeholders
                var (restoredText, allRestored) = t.RestorePlaceholders(placeholders);
                
                translated = detectedLang == TranslateLanguage ? text : restoredText;
                
                if (!allRestored)
                {
                    Log.Warning(AutoTranslation.LogPrefix + $"{Name}: Some placeholders were not properly restored. Using original text.");
                    translated = text;
                    return false;
                }
                
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

        public override bool SupportsCurrentLanguage()
        {
            var lang = LanguageDatabase.activeLanguage;
            if (lang == null)
            {
                Log.Warning(AutoTranslation.LogPrefix + "activeLanguage was null");
                return false;
            }

            return TranslateLanguageGetter.TryGetValue(lang.LegacyFolderName, out var _);
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

            // Simple regex to extract language, fragile but matches existing logic
            // The existing regex was: @"\[""([^""]+)""\]\]\]"
            // The detected lang is usually at the end of the JSON array for client=gtx
            // [[["translated","orig",...]], ... "en"]
            // The regex looks for ["code"]]] at the end? 
            // Original code: detectedLang = "aaaaa"; // match.Success ? ...
            // It seems detection was disabled/commented out in original code?
            // "detectedLang = "aaaaa"; /*match.Success ? match.Groups[1].Value : string.Empty;*/"
            // So I will leave it as is.
            
            detectedLang = "aaaaa"; 

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

            var lang = LanguageDatabase.activeLanguage.LegacyFolderName;

            lang = lang.Split('_').First();

            if (!TranslateLanguageGetter.TryGetValue(lang, out var res))
            {
                Log.Error(AutoTranslation.LogPrefix + $"Unsupported language: {LanguageDatabase.activeLanguage.LegacyFolderName}");
                res = "en";
            }

            return res;
        }
    }
}
