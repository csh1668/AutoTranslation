using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RimWorld;
using UnityEngine;
using Verse;

namespace AutoTranslation.Translators
{
    public abstract class Translator_BaseOnlineAIModel : ITranslator
    {
        public abstract string Name { get; }
        public bool Ready { get; set; }
        public virtual bool RequiresKey => true;

        public TranslatorSettings Settings { get; set; }
        public TranslatorSettings_AIModel Config
        {
            get
            {
                if (Settings == null) Settings = new TranslatorSettings_AIModel();
                return Settings as TranslatorSettings_AIModel;
            }
        }

        public virtual string Model => _model ?? (_model = Config?.UserSelectedModel);
        public List<string> Models
        {
            get
            {
                if (_models != null) return _models;
                
                try
                {
                    var result = GetModels();
                    
                    // Check if GetModels() returned null (indicating failure)
                    if (result == null)
                    {
                        var msg = AutoTranslation.LogPrefix + $"{Name}: Failed to load models (returned null)";
                        Log.ErrorOnce(msg, msg.GetHashCode());
                        _models = new List<string>(); // Store empty list to prevent retries
                        return _models;
                    }
                    
                    _models = result;
                    return _models;
                }
                catch (Exception e)
                {
                    var msg = AutoTranslation.LogPrefix + $"{Name}: Failed to load models: {e.Message}";
                    Log.ErrorOnce(msg, msg.GetHashCode());
                    _models = new List<string>(); // Store empty list to prevent retries
                    return _models;
                }
            }
        }

        public abstract string BaseURL { get; }

        public virtual void Prepare()
        {
            if (Settings == null) Settings = new TranslatorSettings_AIModel();
            
            if (string.IsNullOrEmpty(Config.UserAPIKey) && RequiresKey) return;
            Ready = true;
        }

        public bool TryTranslate(string text, out string translated)
        {
            return TryTranslate(text, out translated, false);
        }

        public bool TryTranslate(string text, out string translated, bool skipRetry)
        {
            if (string.IsNullOrEmpty(text))
            {
                translated = string.Empty;
                return true;
            }

            if (string.IsNullOrEmpty(Model))
            {
                var msg = AutoTranslation.LogPrefix + $"{Name}: Model is not set!";
                Log.ErrorOnce(msg, msg.GetHashCode());
                translated = text;
                return false;
            }

            // skipRetry가 true이면 재시도하지 않음 (maxRetries = 0)
            var maxRetries = skipRetry ? 0 : 2;
            var retryCount = 0;

            while (retryCount <= maxRetries)
            {
                try
                {
                    // Phase 1: Protect placeholders
                    var (protectedText, placeholders) = text.ProtectPlaceholders();

                    // Phase 2: AI Translation - Pass prompt explicitly
                    var prompt = GetNormalPrompt();
                    var translatedProtected = ParseResponse(GetResponseUnsafe(protectedText, prompt));

                    // Phase 3: Restore placeholders
                    var (restoredText, allRestored) = translatedProtected.RestorePlaceholders(placeholders);

                    // Validation
                    if (allRestored && text.ValidatePlaceholderCount(restoredText))
                    {
                        translated = restoredText;
                        
                        if (retryCount > 0)
                        {
                            Log.Message(AutoTranslation.LogPrefix + $"{Name}: Successfully translated after {retryCount} retries.");
                        }
                        
                        return true;
                    }
                    else
                    {
                        // Validation failed
                        retryCount++;
                        
                        if (retryCount <= maxRetries)
                        {
                            Log.Warning(AutoTranslation.LogPrefix + 
                                $"{Name}: Placeholder restoration/validation failed (attempt {retryCount}/{maxRetries}). Retrying...");
                            Thread.Sleep(500); // Brief delay before retry
                            continue;
                        }
                        else
                        {
                            // Max retries reached, return original
                            Log.Warning(AutoTranslation.LogPrefix + 
                                $"{Name}: Failed to translate after {maxRetries} retries. Placeholder mismatch detected. Returning original text.");
                            translated = text;
                            return false;
                        }
                    }
                }
                catch (Exception e)
                {
                    retryCount++;
                    
                    if (retryCount <= maxRetries)
                    {
                        Log.Warning(AutoTranslation.LogPrefix + 
                            $"{Name}: Translation exception (attempt {retryCount}/{maxRetries}): {e.Message}. Retrying...");
                        Thread.Sleep(500);
                        continue;
                    }
                    else
                    {
                        var msg = AutoTranslation.LogPrefix + $"{Name}, translate failed after {maxRetries} retries. reason: {e.GetType()}|{e.Message}";
                        Log.WarningOnce(msg + $", target: {text}", msg.GetHashCode());
                        translated = text;
                        return false;
                    }
                }
            }

            // Should never reach here, but just in case
            translated = text;
            return false;
        }

        // Batch translation support
        public virtual bool SupportsBatchTranslation => true;

        public bool TryTranslateBatch(List<string> texts, out List<string> translated)
        {
            translated = new List<string>();
            
            if (texts == null || texts.Count == 0)
            {
                return true;
            }

            if (string.IsNullOrEmpty(Model))
            {
                var msg = AutoTranslation.LogPrefix + $"{Name}: Model is not set!";
                Log.ErrorOnce(msg, msg.GetHashCode());
                translated = new List<string>(texts);
                return false;
            }

            try
            {
                // Phase 1: Protect placeholders for each text
                var protectedTexts = new List<string>();
                var allPlaceholders = new List<Dictionary<string, string>>();
                
                foreach (var text in texts)
                {
                    var (protectedText, placeholders) = text.ProtectPlaceholders();
                    protectedTexts.Add(protectedText);
                    allPlaceholders.Add(placeholders);
                }

                // Phase 2: Build XML batch request
                var batchXml = BuildBatchXml(protectedTexts);

                // Phase 3: Send batch request to AI with BatchPrompt
                // Pass batch prompt as local parameter (thread-safe)
                var batchPrompt = GetBatchPrompt();
                var response = ParseResponse(GetResponseUnsafe(batchXml, batchPrompt));

                // Phase 4: Parse XML response
                var translatedTexts = ParseBatchXml(response, protectedTexts.Count);
                
                if (translatedTexts == null || translatedTexts.Count != protectedTexts.Count)
                {
                    Log.Warning(AutoTranslation.LogPrefix + 
                        $"{Name}: Batch translation failed - response count mismatch. Expected {protectedTexts.Count}, got {translatedTexts?.Count ?? 0}");
                    translated = new List<string>(texts);
                    return false;
                }

                // Phase 5: Restore placeholders for each translated text
                var results = new List<string>();
                var allSuccess = true;
                
                for (int i = 0; i < translatedTexts.Count; i++)
                {
                    var (restoredText, allRestored) = translatedTexts[i].RestorePlaceholders(allPlaceholders[i]);
                    
                    if (allRestored && texts[i].ValidatePlaceholderCount(restoredText))
                    {
                        results.Add(restoredText);
                    }
                    else
                    {
                        Log.Warning(AutoTranslation.LogPrefix + 
                            $"{Name}: Placeholder restoration failed for text #{i} in batch");
                        results.Add(texts[i]); // Return original on failure
                        allSuccess = false;
                    }
                }

                translated = results;
                return allSuccess;
            }
            catch (Exception e)
            {
                var msg = AutoTranslation.LogPrefix + $"{Name}: Batch translation failed: {e.GetType()}|{e.Message}";
                Log.Warning(msg);
                translated = new List<string>(texts);
                return false;
            }
        }

        private string BuildBatchXml(List<string> texts)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<translations>");
            
            for (int i = 0; i < texts.Count; i++)
            {
                // Escape XML special characters
                var escapedText = texts[i]
                    .Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;")
                    .Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
                
                sb.AppendLine($"  <text id=\"{i}\">{escapedText}</text>");
            }
            
            sb.AppendLine("</translations>");
            return sb.ToString();
        }

        private List<string> ParseBatchXml(string response, int expectedCount)
        {
            var results = new List<string>();
            
            try
            {
                if (string.IsNullOrEmpty(response))
                {
                    Log.Warning(AutoTranslation.LogPrefix + "Failed to parse batch XML response - response is null or empty");
                    return null;
                }
                
                // Decode Unicode escape sequences (e.g., \u003c becomes <)
                var cleanedResponse = Regex.Unescape(response);
                
                // Clean response - remove markdown code blocks if present
                if (cleanedResponse.Contains("```xml") || cleanedResponse.Contains("```"))
                {
                    cleanedResponse = Regex.Replace(cleanedResponse, @"```xml\s*", "", RegexOptions.IgnoreCase);
                    cleanedResponse = Regex.Replace(cleanedResponse, @"```\s*", "");
                }
                
                // Extract text elements using regex
                var pattern = @"<text\s+id\s*=\s*[""'](\d+)[""']\s*>(.*?)</text>";
                var matches = Regex.Matches(cleanedResponse, pattern, RegexOptions.Singleline | RegexOptions.IgnoreCase);
                
                if (matches.Count == 0)
                {
                    Log.Warning(AutoTranslation.LogPrefix + "Failed to parse batch XML response - no text elements found");
                    return null;
                }

                // Sort by id to maintain order
                var sortedMatches = matches.Cast<Match>()
                    .OrderBy(m => int.Parse(m.Groups[1].Value))
                    .ToList();

                foreach (var match in sortedMatches)
                {
                    var content = match.Groups[2].Value;
                    
                    // Unescape XML entities
                    content = content
                        .Replace("&lt;", "<")
                        .Replace("&gt;", ">")
                        .Replace("&quot;", "\"")
                        .Replace("&apos;", "'")
                        .Replace("&amp;", "&");
                    
                    results.Add(content.Trim());
                }

                return results;
            }
            catch (Exception e)
            {
                Log.Warning(AutoTranslation.LogPrefix + $"Error parsing batch XML: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets the batch translation prompt (thread-safe, computed on-demand)
        /// </summary>
        protected string GetBatchPrompt()
        {
            var targetLanguage = LanguageDatabase.activeLanguage?.LegacyFolderName ?? "English";
            
            return $"You are translating RimWorld game content into natural {targetLanguage}.\n\n" +
                "TASK: Translate the XML document below. Each <text> element contains content to translate.\n\n" +
                "CRITICAL RULES:\n" +
                "1. PRESERVE all __PH[number]__ tokens exactly as they appear (these are placeholders)\n" +
                "2. PRESERVE all XML tags and id attributes exactly\n" +
                "3. ONLY translate the content inside <text> tags\n" +
                "4. Keep the same XML structure and formatting\n" +
                "5. Output ONLY the translated XML, nothing else\n\n" +
                "EXAMPLE INPUT:\n" +
                "<translations>\n" +
                "  <text id=\"0\">Welcome to __PH0__</text>\n" +
                "  <text id=\"1\">Hello World</text>\n" +
                "</translations>\n\n" +
                $"EXAMPLE OUTPUT ({targetLanguage}):\n" +
                "<translations>\n" +
                $"  <text id=\"0\">{(targetLanguage == "Korean" ? "__PH0__에 오신 것을 환영합니다" : "Welcome to __PH0__")}</text>\n" +
                $"  <text id=\"1\">{(targetLanguage == "Korean" ? "안녕하세요" : "Hello World")}</text>\n" +
                "</translations>\n\n" +
                "Now translate the following:";
        }

        public abstract List<string> GetModels();

        public void ResetSettings()
        {
            _model = null;
            _models = null;
            _rotater = null;
            _baseURL = null;
            Prepare();
        }

        /// <summary>
        /// Force reload models list (used when user clicks the dropdown button)
        /// </summary>
        private void RefreshModels()
        {
            _models = null;
        }

        /// <summary>
        /// Core API call method. MUST be overridden to accept prompt parameter.
        /// </summary>
        protected abstract string GetResponseUnsafe(string text, string prompt);

        protected virtual string ParseResponse(string response)
        {
            return response.GetStringValueFromJson("text");
        }

        /// <summary>
        /// Gets the base translation prompt template (thread-safe, computed on-demand)
        /// </summary>
        protected string GetBasePrompt()
        {
            return $"Translate the following text into natural {LanguageDatabase.activeLanguage?.LegacyFolderName ?? "English"} suitable for RimWorld game context.\n\n" +
                "CRITICAL RULES:\n" +
                "1. PRESERVE all tokens in the format __PH[number]__ exactly as they appear.\n" +
                "2. Do NOT translate, remove, or modify __PH[number]__ tokens.\n" +
                "3. Output ONLY the translated text, no explanations or additional text.\n" +
                "4. Maintain the same tone and formality as the original.";
        }

        /// <summary>
        /// Gets the normal (non-batch) prompt, respecting user customization (thread-safe)
        /// </summary>
        protected string GetNormalPrompt()
        {
            var customPrompt = Config?.UserCustomPrompt?.Trim();
            return string.IsNullOrEmpty(customPrompt) ? GetBasePrompt() : customPrompt;
        }

        protected string APIKey =>
            _rotater == null ? (_rotater = new APIKeyRotater(Config?.UserAPIKey?.Split(',') ?? new string[0])).Key : _rotater.Key;

        protected string RequestURL
        {
            get
            {
                if (_baseURL == null)
                {
                    var url = Config?.UserCustomBaseURL;
                    if (string.IsNullOrEmpty(url))
                    {
                        url = BaseURL;
                    }
                    
                    if (!url.EndsWith("/"))
                    {
                        url += "/";
                    }

                    _baseURL = url;
                }

                return _baseURL;
            }
        }


        protected APIKeyRotater _rotater = null;

        private List<string> _models;
        private string _model = null;
        private string _baseURL = null;

        public void DrawSettings(Listing_Standard ls)
        {
            if (Settings == null) Settings = new TranslatorSettings_AIModel();

            var apiKeyLabelRect = ls.GetRect(Text.LineHeight);
            Widgets.Label(apiKeyLabelRect, "AT_Setting_APIKey".Translate());
            TooltipHandler.TipRegion(apiKeyLabelRect, "AT_Setting_RequiresAPIKey_Tooltip".Translate());
            
            Config.UserAPIKey = ls.TextEntry(Config.UserAPIKey);

            ls.Gap();

            ls.Label("AT_Setting_Model".Translate());
            
            // Button click refreshes model list
            if (Widgets.ButtonText(ls.GetRect(30f), string.IsNullOrEmpty(Config.UserSelectedModel) ? "AT_ChooseModel".Translate().ToString() : Config.UserSelectedModel))
            {
                // Force refresh models when button is clicked
                RefreshModels();
                
                // Get fresh model list
                var modelList = Models;
                
                if (modelList != null && modelList.Count > 0)
                {
                    var options = new List<FloatMenuOption>();
                    foreach (var m in modelList)
                    {
                        options.Add(new FloatMenuOption(m, () => Config.UserSelectedModel = m));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
                else
                {
                    Messages.Message("AT_Message_NoModelsFound".Translate(), MessageTypeDefOf.NegativeEvent);
                }
            }
            
            // Show text entry if models is empty (as fallback for manual entry)
            // Don't use Models property here to avoid triggering API call every frame
            if (_models != null && _models.Count == 0)
            {
                Config.UserSelectedModel = ls.TextEntry(Config.UserSelectedModel);
            }

            ls.Gap();
            
            ls.Label("AT_Setting_CustomBaseURL".Translate());
            Config.UserCustomBaseURL = ls.TextEntry(Config.UserCustomBaseURL);

            ls.Gap();

            ls.Label("AT_Setting_CustomPrompt".Translate());
            Config.UserCustomPrompt = ls.TextEntry(Config.UserCustomPrompt, 3);
            
            ls.Gap();

            // Batch translation settings
            var batchCheckRect = ls.GetRect(Text.LineHeight);
            Widgets.CheckboxLabeled(batchCheckRect, "AT_Setting_EnableBatchTranslation".Translate(), ref Config.EnableBatchTranslation);
            TooltipHandler.TipRegion(batchCheckRect, "AT_Setting_EnableBatchTranslation_Tooltip".Translate());
            
            if (Config.EnableBatchTranslation)
            {
                ls.Gap(6f);
                
                var batchSizeLabelRect = ls.GetRect(Text.LineHeight);
                Widgets.Label(batchSizeLabelRect, "AT_Setting_BatchSize".Translate() + $": {Config.BatchSizeTokens}");
                TooltipHandler.TipRegion(batchSizeLabelRect, "AT_Setting_BatchSize_Tooltip".Translate());
                
                // Slider with 100-unit snapping
                var newValue = Widgets.HorizontalSlider(
                    ls.GetRect(22f),
                    Config.BatchSizeTokens,
                    500f,
                    5000f,
                    true,
                    null,
                    "500",
                    "5000",
                    100f
                );
                
                // Snap to nearest 100
                Config.BatchSizeTokens = Mathf.RoundToInt(newValue / 100f) * 100;
                
                ls.Gap(6f);
            }
            
            ls.Gap();

            if (ls.ButtonText("AT_Setting_Reset".Translate()))
            {
                // Reset to defaults
                Config.UserAPIKey = "";
                Config.UserSelectedModel = "";
                Config.UserCustomBaseURL = "";
                Config.UserCustomPrompt = "";
                Config.EnableBatchTranslation = true;
                Config.BatchSizeTokens = 2000;
                ResetSettings();
            }
        }
    }
}
