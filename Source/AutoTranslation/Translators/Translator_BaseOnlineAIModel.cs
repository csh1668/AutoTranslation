using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimWorld;
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

            const int maxRetries = 2;
            var retryCount = 0;

            while (retryCount <= maxRetries)
            {
                try
                {
                    // Phase 1: Protect placeholders
                    var (protectedText, placeholders) = text.ProtectPlaceholders();

                    // Phase 2: AI Translation
                    var translatedProtected = ParseResponse(GetResponseUnsafe(protectedText));

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

        public abstract List<string> GetModels();

        public void ResetSettings()
        {
            _model = null;
            _models = null;
            _rotater = null;
            _prompt = null;
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

        protected abstract string GetResponseUnsafe(string text);

        protected virtual string ParseResponse(string response)
        {
            return response.GetStringValueFromJson("text");
        }

        protected string BasePrompt => 
            $"Translate the following text into natural {LanguageDatabase.activeLanguage?.LegacyFolderName ?? "English"} suitable for RimWorld game context.\n\n" +
            "CRITICAL RULES:\n" +
            "1. PRESERVE all tokens in the format __PH[number]__ exactly as they appear.\n" +
            "2. Do NOT translate, remove, or modify __PH[number]__ tokens.\n" +
            "3. Output ONLY the translated text, no explanations or additional text.\n" +
            "4. Maintain the same tone and formality as the original.";

        protected string APIKey =>
            _rotater == null ? (_rotater = new APIKeyRotater(Config?.UserAPIKey?.Split(',') ?? new string[0])).Key : _rotater.Key;
        protected string Prompt => _prompt ?? (_prompt = string.IsNullOrEmpty(Config?.UserCustomPrompt?.Trim()) ? BasePrompt : Config.UserCustomPrompt.Trim());

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
        private string _prompt = null;
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

            if (ls.ButtonText("AT_Setting_Reset".Translate()))
            {
                // Reset to defaults
                Config.UserAPIKey = "";
                Config.UserSelectedModel = "";
                Config.UserCustomBaseURL = "";
                Config.UserCustomPrompt = "";
                ResetSettings();
            }
        }
    }
}
