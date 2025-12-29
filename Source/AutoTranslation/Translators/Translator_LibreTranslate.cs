using Verse;
using UnityEngine;
using RimWorld;
using AutoTranslation;
using System.Collections.Generic;
using System.Linq;
using System;

namespace AutoTranslation.Translators
{
    public class Translator_LibreTranslate : Translator_BaseTraditional
    {
        public override string Name => "LibreTranslate";

        public override bool RequiresKey => false;

        public override string TranslateLanguage => _cachedTranslateLanguage ?? (_cachedTranslateLanguage = GetTranslateLanguage());
        private string _cachedTranslateLanguage;

        public TranslatorSettings_LibreTranslate Config
        {
            get
            {
                if (Settings == null) Settings = new TranslatorSettings_LibreTranslate();
                return Settings as TranslatorSettings_LibreTranslate;
            }
        }

        public override void Prepare()
        {
            if (Settings == null) Settings = new TranslatorSettings_LibreTranslate();
            Ready = true;
        }

        public override bool TryTranslate(string text, out string translated)
        {
            try
            {
                // Protect placeholders
                var (protectedText, placeholders) = text.ProtectPlaceholders();
                
                var url = Config.CustomUrl.TrimEnd('/') + "/translate";
                var body = $@"{{
                    ""q"": ""{protectedText.EscapeJsonString()}"",
                    ""source"": ""auto"",
                    ""target"": ""{TranslateLanguage}"",
                    ""format"": ""text"",
                    ""api_key"": ""{Config.APIKey}""
                }}";

                var headers = new Dictionary<string, string>();
                headers["Content-Type"] = "application/json";

                var response = NetworkHelper.Post(url, body, headers);
                var translatedProtected = response.GetStringValueFromJson("translatedText");
                
                if (string.IsNullOrEmpty(translatedProtected))
                {
                    translated = text;
                    return false;
                }
                
                // Restore placeholders
                var (restoredText, allRestored) = translatedProtected.RestorePlaceholders(placeholders);
                translated = restoredText;
                
                if (!allRestored)
                {
                    Log.Warning($"{AutoTranslation.LogPrefix} {Name}: Some placeholders were not properly restored. Using original text.");
                    translated = text;
                    return false;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning($"{AutoTranslation.LogPrefix} LibreTranslate failed: {ex.Message}");
                translated = text;
                return false;
            }
        }

        public override bool SupportsCurrentLanguage()
        {
            return TranslateLanguage != "en"; // Support all mapped languages
        }

        // Custom Settings UI
        public override void DrawSettings(Listing_Standard ls)
        {
            if (Settings == null) Settings = new TranslatorSettings_LibreTranslate();

            ls.Label("LibreTranslate URL (Default: https://libretranslate.com)");
            Config.CustomUrl = ls.TextEntry(Config.CustomUrl);
            
            var apiKeyLabelRect = ls.GetRect(Text.LineHeight);
            Widgets.Label(apiKeyLabelRect, "API Key (Optional)");
            TooltipHandler.TipRegion(apiKeyLabelRect, "AT_Setting_RequiresAPIKey_Tooltip".Translate());
            
            Config.APIKey = ls.TextEntry(Config.APIKey);
        }

        // Language Mapping
        private static string GetTranslateLanguage()
        {
            if (LanguageDatabase.activeLanguage == null) return "en";
            var lang = LanguageDatabase.activeLanguage.LegacyFolderName.Split('_').First();
            return _languageMap.TryGetValue(lang, out var res) ? res : "en";
        }

        private static readonly Dictionary<string, string> _languageMap = new Dictionary<string, string>
        {
            // RimWorld supported languages
            ["Korean"] = "ko",
            ["ChineseSimplified"] = "zh-Hans",
            ["ChineseTraditional"] = "zh-Hant",
            ["English"] = "en",
            ["French"] = "fr",
            ["German"] = "de",
            ["Italian"] = "it",
            ["Japanese"] = "ja",
            ["Polish"] = "pl",
            ["Portuguese"] = "pt",
            ["PortugueseBrazilian"] = "pt-BR",
            ["Russian"] = "ru",
            ["Spanish"] = "es",
            ["SpanishLatin"] = "es",
            ["Turkish"] = "tr",
            ["Czech"] = "cs",
            ["Danish"] = "da",
            ["Dutch"] = "nl",
            ["Finnish"] = "fi",
            ["Norwegian"] = "nb",
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
            
            // Additional LibreTranslate supported languages
            ["Albanian"] = "sq",
            ["Azerbaijani"] = "az",
            ["Bulgarian"] = "bg",
            ["Bengali"] = "bn",
            ["Basque"] = "eu",
            ["Esperanto"] = "eo",
            ["Persian"] = "fa",
            ["Irish"] = "ga",
            ["Galician"] = "gl",
            ["Hebrew"] = "he",
            ["Hindi"] = "hi",
            ["Kyrgyz"] = "ky",
            ["Lithuanian"] = "lt",
            ["Latvian"] = "lv",
            ["Malay"] = "ms",
            ["Slovenian"] = "sl",
            ["Tagalog"] = "tl",
            ["Urdu"] = "ur"
        };
    }
}
