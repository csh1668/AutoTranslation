using Verse;
using UnityEngine;
using RimWorld;
using AutoTranslation;
using System.Collections.Generic;
using System.Linq;
using System;
using AutoTranslation.Utilities;

namespace AutoTranslation.Translators
{
    public class Translator_LibreTranslate : Translator_BaseTraditional
    {
        public override string Name => "LibreTranslate";

        public override bool RequiresKey => false;

        // Not cached: the user can change the target language override at runtime
        public override string TranslateLanguage => GetTranslateLanguage();

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
                
                // Build JSON body - only include api_key if it's not empty
                var apiKeyField = string.IsNullOrEmpty(Config.APIKey)
                    ? ""
                    : $@",
                    ""api_key"": ""{Config.APIKey.EscapeJsonString()}""";
                
                var body = $@"{{
                    ""q"": ""{protectedText.EscapeJsonString()}"",
                    ""source"": ""auto"",
                    ""target"": ""{TranslateLanguage}"",
                    ""format"": ""text""{apiKeyField}
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

                Config.UsageCharacters += text.Length;
                global::AutoTranslation.Settings.UsageDirty = true;

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
                // Surface the server's error body (e.g. the official instance now
                // returns 400 "Visit portal.libretranslate.com to get an API key")
                var reason = NetworkHelper.ExtractErrorMessage(ex);
                var msg = $"{AutoTranslation.LogPrefix} {Name} failed: {reason}";
                Log.WarningOnce(msg, msg.GetHashCode());
                translated = text;
                return false;
            }
        }

        public override bool SupportsCurrentLanguage()
        {
            if (Helpers.HasLanguageOverride()) return true;
            return TranslateLanguage != "en"; // Support all mapped languages
        }

        // Custom Settings UI
        public override void DrawSettings(Listing_Standard ls)
        {
            if (Settings == null) Settings = new TranslatorSettings_LibreTranslate();

            ls.Label("LibreTranslate URL (Default: https://libretranslate.com)");

            var noticeRect = ls.GetRect(Text.LineHeight);
            var prevColor = GUI.color;
            GUI.color = Color.yellow;
            Widgets.Label(noticeRect, "AT_Setting_LibreTranslateKeyNotice".Translate());
            GUI.color = prevColor;

            Config.CustomUrl = ls.TextEntry(Config.CustomUrl);
            
            var apiKeyLabelRect = ls.GetRect(Text.LineHeight);
            Widgets.Label(apiKeyLabelRect, "API Key (Optional)");
            TooltipHandler.TipRegion(apiKeyLabelRect, "AT_Setting_RequiresAPIKey_Tooltip".Translate());

            Config.APIKey = ls.TextEntry(Config.APIKey);

            ls.GapLine();
            ls.Label("AT_Setting_CostTracking".Translate());
            ls.Label("AT_Setting_UsageCharacters".Translate(Config.UsageCharacters.ToString("N0")));

            var priceRect = ls.GetRect(Text.LineHeight + 4f);
            Widgets.Label(priceRect.LeftPart(0.6f), "AT_Setting_PricePerMChars".Translate());
            Config.PricePerMChars = Helpers.DecimalTextField(priceRect.RightPart(0.38f), $"{Name}_PricePerMChars", Config.PricePerMChars);

            var cost = Config.UsageCharacters / 1_000_000.0 * Config.PricePerMChars;
            ls.Label("AT_Setting_EstimatedCost".Translate(cost.ToString("F4")));

            if (ls.ButtonText("AT_Setting_ResetUsage".Translate()))
            {
                Config.UsageCharacters = 0;
                global::AutoTranslation.Settings.UsageDirty = true;
            }
        }

        // Language Mapping
        private static string GetTranslateLanguage()
        {
            return Helpers.ResolveTargetLanguage(_languageMap) ?? "en";
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
