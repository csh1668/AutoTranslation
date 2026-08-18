/*
Original code: https://github.com/HIllya51/LunaTranslator/blob/main/src/LunaTranslator/translator/yandex.py
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine.Networking;
using Verse;
using AutoTranslation;
using AutoTranslation.Utilities;

namespace AutoTranslation.Translators
{
    public class Translator_Yandex : Translator_BaseTraditional
    {
        private const string translateUrl = "https://browser.translate.yandex.net/api/v1/tr.json/translate";

        public override string Name => "Yandex";

        // Not cached: the user can change the target language override at runtime
        public override string TranslateLanguage => GetTranslateLanguage();
        public override bool RequiresKey => false;

        public override void Prepare()
        {
            // No connectivity preflight (it blocked game loading when offline);
            // real requests + the circuit breaker handle reachability.
            Ready = true;
        }

        public override bool TryTranslate(string text, out string translated)
        {
            try
            {
                // Protect placeholders
                var (protectedText, placeholders) = text.ProtectPlaceholders();
                
                // Yandex auto-detects source language, only specify target language
                var translateParams = $"?lang={UnityWebRequest.EscapeURL(TranslateLanguage)}&text={UnityWebRequest.EscapeURL(protectedText)}&srv=browser_video_translation";
                
                var headers = new Dictionary<string, string>();
                
                // Send POST request with empty body and no Content-Type (Yandex doesn't require body parameters)
                var response = NetworkHelper.Post(translateUrl + translateParams, "", headers, contentType: null);
                
                // Parse response - Yandex returns {"code": 200, "lang": "ja-en", "text": ["translated text"]}
                // The string body must allow escape pairs (\" etc.) or translations containing
                // quotes would fail to match at all
                var textArrayPattern = @"""text""\s*:\s*\[\s*""((?:\\.|[^""\\])*)""";
                var match = Regex.Match(response, textArrayPattern);

                if (!match.Success || match.Groups.Count < 2)
                {
                    translated = text;
                    return false;
                }

                var translatedProtected = match.Groups[1].Value.UnescapeJsonString();
                
                // Restore placeholders
                var (restoredText, allRestored) = translatedProtected.RestorePlaceholders(placeholders);
                translated = restoredText;
                
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
            if (Helpers.HasLanguageOverride()) return true;

            var lang = LanguageDatabase.activeLanguage;
            if (lang == null)
            {
                Log.Warning(AutoTranslation.LogPrefix + "activeLanguage was null");
                return false;
            }

            return TranslateLanguageGetter.ContainsKey(lang.LegacyFolderName.NormalizeLanguageFolder());
        }

        private static readonly Dictionary<string, string> TranslateLanguageGetter = new Dictionary<string, string>
        {
            // RimWorld supported languages with Yandex language codes
            ["Korean"] = "ko",
            ["ChineseSimplified"] = "zh",
            ["ChineseTraditional"] = "zh",
            ["English"] = "en",
            ["French"] = "fr",
            ["German"] = "de",
            ["Italian"] = "it",
            ["Japanese"] = "ja",
            ["Polish"] = "pl",
            ["Portuguese"] = "pt",
            ["PortugueseBrazilian"] = "pt",
            ["Russian"] = "ru",
            ["Spanish"] = "es",
            ["SpanishLatin"] = "es",
            ["Turkish"] = "tr",
            ["Czech"] = "cs",
            ["Danish"] = "da",
            ["Dutch"] = "nl",
            ["Finnish"] = "fi",
            ["Norwegian"] = "no",
            ["Swedish"] = "sv",
            ["Ukrainian"] = "uk",
            ["Hungarian"] = "hu",
            ["Arabic"] = "ar",
            ["Romanian"] = "ro",
            ["Slovak"] = "sk",
            ["Estonian"] = "et",
            ["Greek"] = "el",
            ["Thai"] = "th",
            ["Indonesian"] = "id",
            ["Catalan"] = "ca",
            ["Vietnamese"] = "vi",
            ["Serbian"] = "sr",
            
            // Additional Yandex supported languages
            ["Albanian"] = "sq",
            ["Armenian"] = "hy",
            ["Azerbaijani"] = "az",
            ["Basque"] = "eu",
            ["Belarusian"] = "be",
            ["Bulgarian"] = "bg",
            ["Croatian"] = "hr",
            ["Georgian"] = "ka",
            ["Hebrew"] = "he",
            ["Hindi"] = "hi",
            ["Kazakh"] = "kk",
            ["Latvian"] = "lv",
            ["Lithuanian"] = "lt",
            ["Macedonian"] = "mk",
            ["Mongolian"] = "mn",
            ["Persian"] = "fa",
            ["Slovenian"] = "sl",
            ["Tajik"] = "tg",
            ["Tatar"] = "tt",
            ["Uzbek"] = "uz"
        };
        
        private static string GetTranslateLanguage()
        {
            var res = Helpers.ResolveTargetLanguage(TranslateLanguageGetter);
            if (res == null)
            {
                Log.Error(AutoTranslation.LogPrefix + $"Unsupported language: {LanguageDatabase.activeLanguage?.LegacyFolderName ?? "(null)"}");
                res = "en";
            }
            return res;
        }
    }
}

