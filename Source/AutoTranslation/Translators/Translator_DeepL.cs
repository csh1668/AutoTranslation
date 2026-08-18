using System.Text.RegularExpressions;
using Verse;
using System.Collections.Generic;
using System.Linq;
using System;
using AutoTranslation;
using AutoTranslation.Services;
using AutoTranslation.Utilities;
using UnityEngine;

namespace AutoTranslation.Translators
{
    public class Translator_DeepL : Translator_BaseTraditional
    {
        private static readonly System.Text.StringBuilder sb = new System.Text.StringBuilder(1024);
        protected virtual string url => $"https://api-free.deepl.com/v2/translate";

        public override string Name => "DeepL";
        public override bool RequiresKey => true;
        // Not cached: the user can change the target language override at runtime
        public override string TranslateLanguage => GetTranslateLanguage();

        public TranslatorSettings_DeepL Config
        {
            get
            {
                if (Settings == null) Settings = new TranslatorSettings_DeepL();
                return Settings as TranslatorSettings_DeepL;
            }
        }

        public override void Prepare()
        {
            if (Settings == null) Settings = new TranslatorSettings_DeepL();

            if (string.IsNullOrEmpty(Config.APIKey))
                return;
            Ready = true;
        }

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
                // Use unified placeholder protection system
                var (protectedText, placeholders) = text.ProtectPlaceholders();
                
                var body = $@"
                    {{
                        ""text"": [""{protectedText.EscapeJsonString()}""],
                        ""target_lang"": ""{TranslateLanguage}"",
                        ""preserve_formatting"": true
                    }}
                ";

                var headers = new Dictionary<string, string>
                {
                    { "Authorization", $"DeepL-Auth-Key {Config.APIKey}" }
                };

                // Use NetworkHelper with retry logic
                var response = NetworkHelper.Post(url, body, headers, "application/json", skipRetry ? 0 : 3);
                
                var translatedProtected = Parse(response, out var detectedLang);

                Config.UsageCharacters += text.Length;
                global::AutoTranslation.Settings.UsageDirty = true;

                // Restore placeholders
                var (restoredText, allRestored) = translatedProtected.RestorePlaceholders(placeholders);

                // detected_source_language is always regionless uppercase (e.g. "PT"), so
                // compare base codes case-insensitively - a full-string compare could never
                // match "PT-PT"/"PT-BR", and override fallback codes are lowercase ("lv")
                var sameLanguage = !string.IsNullOrEmpty(detectedLang) &&
                                   string.Equals(detectedLang.Split('-')[0], TranslateLanguage.Split('-')[0], StringComparison.OrdinalIgnoreCase);
                translated = sameLanguage ? text : restoredText;
                
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
                Log.WarningOnce(msg, msg.GetHashCode());
            }

            translated = text;
            return false;
        }

        public override bool SupportsCurrentLanguage()
        {
            if (Helpers.HasLanguageOverride()) return true;

            var lang = LanguageDatabase.activeLanguage?.LegacyFolderName;
            if (lang == null)
            {
                Log.Warning(AutoTranslation.LogPrefix + "activeLanguage was null");
                return false;
            }

            return _languageMap.ContainsKey(lang.NormalizeLanguageFolder());
        }

        protected APIKeyRotater rotater = null;

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
            var res = Helpers.ResolveTargetLanguage(_languageMap);
            if (res == null)
            {
                Log.Error(AutoTranslation.LogPrefix + $"Unsupported language: {LanguageDatabase.activeLanguage?.LegacyFolderName ?? "(null)"} in DeepL, Please change to another translator.");
                res = "EN-US";
            }
            return res;
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
            // Regionless "EN" as a TARGET is deprecated by DeepL - use EN-US
            ["English"] = "EN-US"
        };
        
        public override void DrawSettings(Listing_Standard ls)
        {
            if (Settings == null) Settings = new TranslatorSettings_DeepL();
            
            var apiKeyLabelRect = ls.GetRect(Text.LineHeight);
            Widgets.Label(apiKeyLabelRect, "AT_Setting_APIKey".Translate());
            TooltipHandler.TipRegion(apiKeyLabelRect, "AT_Setting_RequiresAPIKey_Tooltip".Translate());
            
            Config.APIKey = ls.TextEntry(Config.APIKey);

            // Free keys end with ":fx" and only work against api-free.deepl.com;
            // using one against the Pro host (or vice versa) yields 403
            var key = Config.APIKey?.Split(',')[0].Trim() ?? string.Empty;
            if (key.Length > 0)
            {
                var isFreeKey = key.EndsWith(":fx");
                var isFreeEndpoint = url.Contains("api-free.deepl.com");
                if (isFreeKey != isFreeEndpoint)
                {
                    var prevColor = GUI.color;
                    GUI.color = Color.red;
                    ls.Label(isFreeKey
                        ? "AT_Setting_DeepLFreeKeyOnPro".Translate()
                        : "AT_Setting_DeepLProKeyOnFree".Translate());
                    GUI.color = prevColor;
                }
            }

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
    }
}
