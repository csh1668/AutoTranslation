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
        private string _cachedTranslateLanguage;

        public override string Name => "Yandex";

        public override string TranslateLanguage => _cachedTranslateLanguage ?? (_cachedTranslateLanguage = GetTranslateLanguage());
        public override bool RequiresKey => false;

        public override void Prepare()
        {
            try
            {
                // Test connectivity with a simple translation request
                var testParams = $"?lang=en&text=test&srv=browser_video_translation";
                var resp = NetworkHelper.Post(translateUrl + testParams, "", new Dictionary<string, string>(), contentType: null);
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
                
                // Yandex auto-detects source language, only specify target language
                var translateParams = $"?lang={UnityWebRequest.EscapeURL(TranslateLanguage)}&text={UnityWebRequest.EscapeURL(protectedText)}&srv=browser_video_translation";
                
                var headers = new Dictionary<string, string>();
                
                // Send POST request with empty body and no Content-Type (Yandex doesn't require body parameters)
                var response = NetworkHelper.Post(translateUrl + translateParams, "", headers, contentType: null);
                
                // Parse response - Yandex returns {"code": 200, "lang": "ja-en", "text": ["translated text"]}
                // Extract text array using regex: "text":\["([^"]*)"\]
                var textArrayPattern = @"""text""\s*:\s*\[\s*""([^""]*)""\s*\]";
                var match = Regex.Match(response, textArrayPattern);
                
                if (!match.Success || match.Groups.Count < 2)
                {
                    translated = text;
                    return false;
                }
                
                var translatedProtected = match.Groups[1].Value;
                
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
            var lang = LanguageDatabase.activeLanguage;
            if (lang == null)
            {
                Log.Warning(AutoTranslation.LogPrefix + "activeLanguage was null");
                return false;
            }

            return TranslateLanguageGetter.TryGetValue(lang.LegacyFolderName, out var _);
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

