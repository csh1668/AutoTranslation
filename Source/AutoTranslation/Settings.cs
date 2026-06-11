using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoTranslation.Services;
using AutoTranslation.Translators;
using AutoTranslation.UI;
using AutoTranslation.Utilities;
using UnityEngine;
using Verse;
using Verse.Noise;
using Verse.Sound;
using static HarmonyLib.Code;

namespace AutoTranslation
{
    public class Settings : ModSettings
    {
        public static bool AppendTranslationCompleteTag = false;
        public static string TranslatorName = "Google";
        public static bool ShowOriginal = false;
        public static int MaxConcurrency = 5;
        public static bool EnableLanguageDetection = true; // 언어 감지 활성화 (기본값: true)
        public static HashSet<string> BlackListModPackageIds = new HashSet<string>();

        // Polymorphic settings storage
        public static Dictionary<string, TranslatorSettings> TranslatorSettings = new Dictionary<string, TranslatorSettings>();

        // UI State
        private static SettingsTab _curTab = SettingsTab.General;
        private static Vector2 scrollbarVector = Vector2.zero;
        private static Vector2 translatorTabScrollPosition = Vector2.zero;
        private static string TestText = "Hello, World!";
        private static string TestResultText = string.Empty;
        private static string SearchText = string.Empty;
        
        // Gist UI State
        private static string _gistInputText = "";
        private static bool _gistUploading = false;
        private static bool _gistDownloading = false;
        
        // Translation Editor State
        private static string _editorSearchQuery = "";
        private static Vector2 _editorScrollPosition;
        private static Dictionary<string, Dictionary<string, List<KeyValuePair<string, string>>>> _editorGroupedCache;
        private static Dictionary<string, Dictionary<string, List<KeyValuePair<string, string>>>> _editorFilteredGroupCache;
        private static Dictionary<string, string> _editorModNameCache = new Dictionary<string, string>();
        private static HashSet<string> _editorExpandedGroups = new HashSet<string>();
        private static HashSet<string> _editorExpandedSubGroups = new HashSet<string>();
        private static string _editingKey = null;
        private static string _editingValue = "";
        private static bool _restartRequired = false;
        private const float RowHeight = 35f;
        private const float GroupHeaderHeight = 30f;
        private const float SubGroupHeaderHeight = 28f;
        
        // Performance cache for type determination
        private static Dictionary<string, string> _typeCache = new Dictionary<string, string>();

        private static List<ModContentPack> AllMods => _allModsCached ?? (_allModsCached = LoadedModManager.RunningMods.ToList());
        private static List<ModContentPack> _allModsCached;

        private enum SettingsTab
        {
            General,
            Translator,
            TargetMods,
            TranslationEditor,
            Advanced
        }

        public override void ExposeData()
        {
            base.ExposeData();
            
            // Load current settings
            Scribe_Values.Look(ref AppendTranslationCompleteTag, "AutoTranslation_AppendTranslationCompleteTag", false);
            Scribe_Values.Look(ref TranslatorName, "AutoTranslation_TranslatorName", "Google");
            Scribe_Values.Look(ref ShowOriginal, "AutoTranslation_ShowOriginal", false);
            Scribe_Values.Look(ref MaxConcurrency, "AutoTranslation_MaxConcurrency", 5);
            Scribe_Values.Look(ref EnableLanguageDetection, "AutoTranslation_EnableLanguageDetection", true);
            Scribe_Collections.Look(ref BlackListModPackageIds, "AutoTranslation_WhiteListModPackageIds", LookMode.Value);
            
            // Try to load new format settings
            Scribe_Collections.Look(ref TranslatorSettings, "TranslatorSettings", LookMode.Value, LookMode.Deep);

            // Legacy settings migration (only during loading)
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                string legacyAPIKey = "";
                string legacySelectedModel = "";
                string legacyCustomBaseURL = "";
                string legacyCustomPrompt = "";

                Scribe_Values.Look(ref legacyAPIKey, "AutoTranslation_APIKey", "");
                Scribe_Values.Look(ref legacySelectedModel, "AutoTranslation_SelectedModel", "");
                Scribe_Values.Look(ref legacyCustomBaseURL, "AutoTranslation_CustomBaseURL", "");
                Scribe_Values.Look(ref legacyCustomPrompt, "AutoTranslation_CustomPrompt", "");

                // Perform migration if legacy data exists
                if (TranslatorSettings == null || TranslatorSettings.Count == 0)
                {
                    if (TranslatorSettings == null) TranslatorSettings = new Dictionary<string, TranslatorSettings>();
                    MigrationHelper.MigrateLegacyTranslatorSettings(legacyAPIKey, legacySelectedModel, legacyCustomBaseURL, legacyCustomPrompt);
                }
            }

            if (TranslatorSettings == null) TranslatorSettings = new Dictionary<string, TranslatorSettings>();
            if (BlackListModPackageIds == null) BlackListModPackageIds = new HashSet<string>();
        }

        public void DoSettingsWindowContents(Rect inRect)
        {
            // Tab Header
            List<TabRecord> tabs = new List<TabRecord>();
            tabs.Add(new TabRecord("AT_Tab_General".Translate(), () => _curTab = SettingsTab.General, _curTab == SettingsTab.General));
            tabs.Add(new TabRecord("AT_Tab_Translator".Translate(), () => _curTab = SettingsTab.Translator, _curTab == SettingsTab.Translator));
            tabs.Add(new TabRecord("AT_Tab_TargetMods".Translate(), () => _curTab = SettingsTab.TargetMods, _curTab == SettingsTab.TargetMods));
            tabs.Add(new TabRecord("AT_Tab_TranslationEditor".Translate(), () => _curTab = SettingsTab.TranslationEditor, _curTab == SettingsTab.TranslationEditor));
            tabs.Add(new TabRecord("AT_Tab_Advanced".Translate(), () => _curTab = SettingsTab.Advanced, _curTab == SettingsTab.Advanced));

            // Add gap between title and tabs
            Rect tabRect = new Rect(inRect);
            tabRect.yMin += 50f; // Increased gap to prevent title overlap
            TabDrawer.DrawTabs(new Rect(inRect.x, inRect.y + 15f, inRect.width, 32f), tabs); // Added 15f offset for gap

            // Tab Content
            Rect contentRect = tabRect.ContractedBy(10f);
            
            switch (_curTab)
            {
                case SettingsTab.General:
                    DoGeneralTab(contentRect);
                    break;
                case SettingsTab.Translator:
                    DoTranslatorTab(contentRect);
                    break;
                case SettingsTab.TargetMods:
                    DoTargetModsTab(contentRect);
                    break;
                case SettingsTab.TranslationEditor:
                    DoTranslationEditorTab(contentRect);
                    break;
                case SettingsTab.Advanced:
                    DoAdvancedTab(contentRect);
                    break;
            }
        }

        private void DoGeneralTab(Rect inRect)
        {
            var ls = new Listing_Standard();
            ls.Begin(inRect);
            ls.CheckboxLabeled("AT_Setting_ShowOriginal".Translate(), ref ShowOriginal, "AT_Setting_ShowOriginal_Tooltip".Translate());
            ls.CheckboxLabeled("AT_Setting_EnableLanguageDetection".Translate(), ref EnableLanguageDetection, "AT_Setting_EnableLanguageDetection_Tooltip".Translate());
            
            if (Prefs.DevMode)
            {
                ls.CheckboxLabeled("AT_Setting_Test".Translate(), ref AppendTranslationCompleteTag);
            }
            
            ls.End();
        }

        private void DoTranslatorTab(Rect inRect)
        {
            // Estimate content height generously to ensure scrollability
            float estimatedContentHeight = 800f; // Base height for typical settings
            
            // Add extra height for AI model settings with batch options
            var targetTranslator = TranslatorManager.GetTranslator(TranslatorName);
            if (targetTranslator is Translator_BaseOnlineAIModel)
            {
                estimatedContentHeight += 200f; // Extra space for batch settings
            }
            
            var viewRect = new Rect(0f, 0f, inRect.width - 16f, estimatedContentHeight);
            Widgets.BeginScrollView(inRect, ref translatorTabScrollPosition, viewRect);
            
            var ls = new Listing_Standard();
            ls.Begin(viewRect);

            ls.Label("AT_Setting_SelectEngine".Translate());
            if (Widgets.ButtonText(ls.GetRect(28f), TranslatorName))
            {
                var list = TranslatorManager.translators.Select(t =>
                    new FloatMenuOption(
                        t.Name,
                        () =>
                        {
                            if (t is Translator_BaseTraditional tr && !tr.SupportsCurrentLanguage())
                            {
                                Messages.Message("AT_Message_LanguageNotSupported".Translate(), MessageTypeDefOf.NegativeEvent);
                            }
                            else
                            {
                                TranslatorName = t.Name;
                            }
                        })).ToList();

                Find.WindowStack.Add(new FloatMenu(list));
            }
            ls.Gap();

            if (targetTranslator != null)
            {
                ls.Label($"--- {targetTranslator.Name} Settings ---");
                ls.Gap();
                
                try
                {
                    targetTranslator.DrawSettings(ls);
                }
                catch (Exception ex)
                {
                    var msg = AutoTranslation.LogPrefix + $"Error drawing settings for {targetTranslator.Name}: {ex.Message}";
                    Log.ErrorOnce(msg, msg.GetHashCode());
                    ls.Label($"<color=red>Error loading settings: {ex.Message}</color>");
                }
                
                ls.GapLine();
                
                // Test Translation Area
                var entryRect = ls.GetRect(28f);
                var left = entryRect.LeftPart(0.33f);
                var mid = new Rect(entryRect.x + left.width, entryRect.y, left.width, entryRect.height);
                var right = new Rect(entryRect.x + left.width + mid.width, entryRect.y, left.width, entryRect.height);

                TestText = Widgets.TextField(left, TestText);

                if (Widgets.ButtonText(mid, "AT_Setting_TestTranslation".Translate()))
                {
                    if (targetTranslator is Translator_BaseOnlineAIModel ait)
                    {
                        // Refresh settings before test if needed? 
                        // With new system, settings are live.
                        ait.Prepare(); // Ensure ready
                    }
                    
                    if (!targetTranslator.TryTranslate(TestText, out TestResultText, true))
                    {
                        Messages.Message("AT_Message_TestFailed".Translate(), MessageTypeDefOf.NegativeEvent);
                        Log.TryOpenLogWindow();
                    }
                }

                Widgets.TextField(right, TestResultText);
            }
            else
            {
                ls.Label("AT_Setting_NoTranslatorError".Translate());
            }

            ls.End();
            Widgets.EndScrollView();
        }

        private void DoTargetModsTab(Rect inRect)
        {
             const float entryHeight = 22f;
            var cntEntry = AllMods.Count;

            // Header (Search & Toggle)
            var headerRect = inRect.TopPartPixels(30f);
            var listRect = new Rect(inRect.x, inRect.y + 35f, inRect.width, inRect.height - 35f);
            
            var searchRect = new Rect(headerRect.x, headerRect.y, 200f, 24f);
            SearchText = Widgets.TextField(searchRect, SearchText);
            
            var toggleRect = new Rect(headerRect.xMax - 150f, headerRect.y, 150f, 24f);
            if (Widgets.ButtonText(toggleRect, "AT_Setting_ToggleAll".Translate()))
            {
                if (BlackListModPackageIds.Count == AllMods.Count)
                {
                    BlackListModPackageIds.Clear();
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                }
                else if (BlackListModPackageIds.Count == 0)
                {
                    foreach (var mod in AllMods) BlackListModPackageIds.Add(mod.PackageId);
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                }
                else
                {
                     if (BlackListModPackageIds.Count < AllMods.Count / 2)
                     {
                         foreach (var mod in AllMods) BlackListModPackageIds.Add(mod.PackageId);
                         SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                     }
                     else
                     {
                         BlackListModPackageIds.Clear();
                         SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                     }
                }
            }

            // List
            var filteredMods = AllMods.Where(m =>
                    string.IsNullOrEmpty(SearchText) || m.Name.ToLower().Contains(SearchText.ToLower()) ||
                    m.PackageId.ToLower().Contains(SearchText.ToLower()))
                .ToList();

            var viewRect = new Rect(0f, 0f, listRect.width - 16f, filteredMods.Count * entryHeight);
            
            Widgets.BeginScrollView(listRect, ref scrollbarVector, viewRect);

            // Virtual scrolling: only render visible items
            int firstVisibleIndex = Mathf.Max(0, Mathf.FloorToInt(scrollbarVector.y / entryHeight));
            int lastVisibleIndex = Mathf.Min(filteredMods.Count - 1, Mathf.CeilToInt((scrollbarVector.y + listRect.height) / entryHeight));
            
            // Cache translation stats to avoid repeated calls
            var translationStats = InjectionManager.GetTranslationStatsByPackageId();

            for (int i = firstVisibleIndex; i <= lastVisibleIndex; i++)
            {
                var curMod = filteredMods[i];
                var entryRect = new Rect(0f, i * entryHeight, viewRect.width, entryHeight);
                
                if (i % 2 == 0) Widgets.DrawLightHighlight(entryRect);
                
                GUI.BeginGroup(entryRect);
                
#if !RW14
                Widgets.ButtonImage(new Rect(0f, 0f, entryHeight, entryHeight), curMod.ModMetaData?.Icon ?? BaseContent.BadTex);
#endif
                
                translationStats.TryGetValue(curMod.PackageId ?? "", out var stats);
                string statText = $"{stats.Item1}/{stats.Item3} + {stats.Item2}/{stats.Item4}";
                
                var tmp = !BlackListModPackageIds.Contains(curMod.PackageId);
                var tmp2 = tmp;
                
                var labelRect = new Rect(entryHeight + 5f, 0f, entryRect.width - entryHeight - 80f, entryHeight);
                Widgets.CheckboxLabeled(labelRect, $"{curMod.Name} ({curMod.PackageId}) - {statText}", ref tmp);
                
                if (tmp != tmp2)
                {
                    if (!tmp)
                    {
                        BlackListModPackageIds.Add(curMod.PackageId);
                        if (TranslatorManager._queue.Count == 0)
                        {
                            InjectionManager.UndoInjectMissingDefInjection(curMod);
                            InjectionManager.UndoInjectMissingKeyed(curMod);
                            ResetDefCaches();
                        }
                        else
                        {
                            Messages.Message("AT_Message_WhiteList_Failed".Translate(), MessageTypeDefOf.NegativeEvent);
                        }
                    }
                    else
                    {
                        BlackListModPackageIds.Remove(curMod.PackageId);
                         if (TranslatorManager._queue.Count == 0)
                        {
                            InjectionManager.InjectMissingDefInjection(curMod);
                            ResetDefCaches();
                        }
                        else
                        {
                            Messages.Message("AT_Message_WhiteList_Failed".Translate(), MessageTypeDefOf.NegativeEvent);
                        }
                    }
                }
                
                // Retranslate Button
                if (BlackListModPackageIds.Contains(curMod.PackageId))
                {
                     var retranslateRect = new Rect(entryRect.width - 75f, 0f, 70f, entryHeight);
                     if (Widgets.ButtonText(retranslateRect, "AT_Setting_Retranslate".Translate()))
                     {
                         if (TranslatorManager._queue.Count == 0)
                         {
                             BlackListModPackageIds.Remove(curMod.PackageId);
                             InjectionManager.RetranslateMod(curMod);
                         }
                         else
                         {
                             Messages.Message("AT_Message_RetranslateFailed".Translate(), MessageTypeDefOf.NegativeEvent);
                         }
                     }
                }

                GUI.EndGroup();
            }
            Widgets.EndScrollView();
        }

        private void DoAdvancedTab(Rect inRect)
        {
            var ls = new Listing_Standard();
            ls.Begin(inRect);
            
            ls.Label("AT_Setting_Misc".Translate());
            ls.GapLine();

            var concurrencyLabel = "AT_Setting_MaxConcurrency".Translate() + ": " + MaxConcurrency;
            MaxConcurrency = (int)ls.SliderLabeled(concurrencyLabel, MaxConcurrency, 1f, 20f);
            
            if (ls.ButtonText("AT_Setting_ResetDefCache".Translate()))
            {
                ResetDefCaches();
                Messages.Message("AT_Message_ResetDefCache".Translate(), MessageTypeDefOf.PositiveEvent);
            }

            ls.GapLine();
            string status;
            if (NetworkStateMonitor.IsOpen)
                status = "AT_Status_NetworkPaused".Translate();
            else if (TranslatorManager._queue.Count > 0)
                status = "AT_Status1".Translate();
            else if (TranslatorManager.workCnt > 20) 
                status = "AT_Status2".Translate();
            else 
                status = "AT_Status3".Translate();
            ls.Label("AT_Setting_CurStatus".Translate() + status);
            ls.Label("AT_Setting_Cached".Translate() + $"{TranslatorManager.CachedTranslationsV2.Count}");
            ls.Label("AT_Setting_NotYet".Translate() + $"{TranslatorManager._queue.Count}");

            if (ls.ButtonText("AT_Setting_ResetTranslationCache".Translate()))
            {
                TranslatorManager.CachedTranslationsV2.Clear();
                TranslatorManager._cacheCount = 0;
                TranslationCacheManager.Clear();
                TranslationCacheManager.Save(nameof(TranslatorManager.CachedTranslationsV2));
                Messages.Message("AT_Message_ResetTranslationCache".Translate(), MessageTypeDefOf.NeutralEvent);
            }

            if (ls.ButtonText("AT_Setting_RestartWork".Translate()))
            {
                var t = TranslatorManager.GetTranslator(TranslatorName);
                if (t != null)
                {
                    try
                    {
                        Log.Message(AutoTranslation.LogPrefix + $"Restarting translation work with {t.Name}...");
                        
                        t.Prepare();
                        
                        if (t.Ready)
                        {
                            Log.Message(AutoTranslation.LogPrefix + "Step 1: Clearing queue...");
                            TranslatorManager.ClearQueue();
                            TranslatorManager.CurrentTranslator = t;

                            Log.Message(AutoTranslation.LogPrefix + "Step 2: Undoing injections...");
                            InjectionManager.UndoInjectAll();
                            
                            Log.Message(AutoTranslation.LogPrefix + "Step 3: Clearing translations...");
                            InjectionManager.ClearDefInjectedTranslations();
                            
                            Log.Message(AutoTranslation.LogPrefix + "Step 4: Clearing reverse translator...");
                            InjectionManager.ReverseTranslator.Clear();
                            
                            Log.Message(AutoTranslation.LogPrefix + "Step 5: Resetting translation counts...");
                            InjectionManager.ResetTranslationCounts();
                            
                            Log.Message(AutoTranslation.LogPrefix + "Step 6: Clearing injection caches...");
                            InjectionManager.ClearInjectionCaches();

                            Log.Message(AutoTranslation.LogPrefix + "Step 7: Resetting Def caches...");
                            ResetDefCaches();
                            
                            Log.Message(AutoTranslation.LogPrefix + "Step 8: Re-finding and injecting all...");
                            InjectionManager.InjectAll();

                            Log.Message(AutoTranslation.LogPrefix + $"Translation work restarted successfully with {t.Name}");
                            Messages.Message("AT_Message_RestartWork".Translate(), MessageTypeDefOf.PositiveEvent);
                        }
                        else
                        {
                            Log.Warning(AutoTranslation.LogPrefix + $"Failed to restart: {t.Name} is not ready");
                            Messages.Message("AT_Message_RestartFailed".Translate(), MessageTypeDefOf.NegativeEvent);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(AutoTranslation.LogPrefix + $"Error restarting translation work: {ex.Message}\n{ex.StackTrace}");
                        Messages.Message($"AT_Message_RestartFailed".Translate() + $": {ex.Message}", MessageTypeDefOf.NegativeEvent);
                    }
                }
                else
                {
                    Log.Error(AutoTranslation.LogPrefix + $"Failed to restart: Translator '{TranslatorName}' not found");
                    Messages.Message("AT_Message_RestartFailed".Translate(), MessageTypeDefOf.NegativeEvent);
                }
            }

            if (ls.ButtonText("AT_Setting_OpenDir".Translate()))
            {
                Application.OpenURL($"file://{TranslationCacheManager.Instance.CacheDirectory}");
            }

            ls.GapLine();
            ls.Label("AT_Gist_SectionTitle".Translate());
            
            // Upload section
            var uploadRect = ls.GetRect(28f);
            if (Widgets.ButtonText(uploadRect, "AT_Gist_UploadButton".Translate()))
            {
                UploadToGist();
            }
            
            if (_gistUploading)
            {
                ls.Label("AT_Gist_Uploading".Translate());
            }
            
            ls.Gap();
            
            // Download section
            ls.Label("AT_Gist_DownloadLabel".Translate());
            var inputRect = ls.GetRect(28f);
            _gistInputText = Widgets.TextField(inputRect, _gistInputText);
            
            var downloadRect = ls.GetRect(28f);
            GUI.enabled = !string.IsNullOrEmpty(_gistInputText) && !_gistDownloading;
            if (Widgets.ButtonText(downloadRect, "AT_Gist_DownloadButton".Translate()))
            {
                DownloadFromGist(_gistInputText);
            }
            GUI.enabled = true;
            
            if (_gistDownloading)
            {
                ls.Label("AT_Gist_Downloading".Translate());
            }

            ls.End();
        }
        
        private static void UploadToGist()
        {
            if (_gistUploading)
                return;
                
            try
            {
                _gistUploading = true;
                
                // Sync local cache to manager
                foreach (var pair in TranslatorManager.CachedTranslationsV2)
                {
                    TranslationCacheManager.AddOrUpdate(pair.Key, pair.Value);
                }
                
                // Serialize translations
                var translations = TranslationCacheManager.Instance.Cache;
                var jsonData = TranslationSerializer.SerializeToJson(translations.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
                
                // Create metadata
                var metadata = new TranslationMetadata
                {
                    TargetLanguage = LanguageDatabase.activeLanguage?.FriendlyNameEnglish ?? "Unknown",
                    TranslatorName = TranslatorManager.CurrentTranslator?.Name ?? "Unknown",
                    TranslatorModel = GetTranslatorModel(),
                    Date = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    TranslationCount = translations.Count
                };
                
                // Upload
                var gistService = new GistService();
                var result = gistService.UploadTranslation(jsonData, metadata);
                
                if (result.Success)
                {
                    // Copy Gist URL to clipboard
                    GUIUtility.systemCopyBuffer = result.GistUrl;
                    Messages.Message($"{"AT_Gist_UploadSuccess".Translate()}\n{"AT_Gist_CopiedToClipboard".Translate()}: {result.GistUrl}", MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    Messages.Message($"{"AT_Gist_UploadFailed".Translate()}: {result.ErrorMessage}", MessageTypeDefOf.NegativeEvent);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{AutoTranslation.LogPrefix}Error uploading to Gist: {ex.Message}");
                Messages.Message($"{"AT_Gist_UploadFailed".Translate()}: {ex.Message}", MessageTypeDefOf.NegativeEvent);
            }
            finally
            {
                _gistUploading = false;
            }
        }
        
        private static void DownloadFromGist(string input)
        {
            if (_gistDownloading || string.IsNullOrEmpty(input))
                return;
                
            try
            {
                _gistDownloading = true;
                
                var gistService = new GistService();
                var result = gistService.DownloadTranslation(input);
                
                if (result.Success)
                {
                    // Show preview dialog first
                    Find.WindowStack.Add(new Dialog_GistPreview(result.Metadata, () =>
                    {
                        // User confirmed, show merge dialog
                        Find.WindowStack.Add(new Dialog_GistMerge(result.Translations));
                    }));
                }
                else
                {
                    Messages.Message($"{"AT_Gist_DownloadFailed".Translate()}: {result.ErrorMessage}", MessageTypeDefOf.NegativeEvent);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{AutoTranslation.LogPrefix}Error downloading from Gist: {ex.Message}");
                Messages.Message($"{"AT_Gist_DownloadFailed".Translate()}: {ex.Message}", MessageTypeDefOf.NegativeEvent);
            }
            finally
            {
                _gistDownloading = false;
            }
        }
        
        private static string GetTranslatorModel()
        {
            var translator = TranslatorManager.CurrentTranslator;
            if (translator == null)
                return "Unknown";
                
            if (translator is Translator_BaseOnlineAIModel aiTranslator)
            {
                var settings = aiTranslator.Settings as TranslatorSettings_AIModel;
                return settings?.UserSelectedModel ?? "Unknown";
            }
            
            return translator.Name;
        }

        private void DoTranslationEditorTab(Rect inRect)
        {
            // Initialize if needed
            if (_editorGroupedCache == null || _editorGroupedCache.Count == 0)
            {
                RefreshEditorFilter();
            }

            var headerRect = inRect.TopPartPixels(40f);
            
            float topOffset = 45f;
            
            // Restart Warning
            if (_restartRequired)
            {
                var warningRect = new Rect(inRect.x, inRect.y + topOffset, inRect.width, 25f);
                var prevColor = GUI.color;
                GUI.color = Color.red;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(warningRect, "AT_Setting_Editor_RestartRequired".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = prevColor;
                
                topOffset += 30f;
            }

            var columnHeaderRect = new Rect(inRect.x, inRect.y + topOffset, inRect.width - 20f, 25f); // Reduce width to prevent overflow
            topOffset += 30f;
            
            var contentRect = new Rect(inRect.x, inRect.y + topOffset, inRect.width, inRect.height - topOffset);

            // Search Bar (adjusted width to accommodate all buttons)
            var searchRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 510f, 30f);
            var newQuery = Widgets.TextField(searchRect, _editorSearchQuery);
            if (newQuery != _editorSearchQuery)
            {
                _editorSearchQuery = newQuery;
                // Mark filtered cache as needing refresh
                _editorFilteredGroupCache = null;
            }

            // Refresh button (smaller width)
            var refreshRect = new Rect(searchRect.xMax + 10f, headerRect.y, 70f, 30f);
            if (Widgets.ButtonText(refreshRect, "AT_Setting_Editor_Refresh".Translate()))
            {
                RefreshEditorFilter();
                Messages.Message("AT_Setting_Editor_Refreshed".Translate(), MessageTypeDefOf.PositiveEvent, false);
            }
            
            // Bulk Replace button
            var bulkReplaceRect = new Rect(refreshRect.xMax + 5f, headerRect.y, 100f, 30f);
            if (Widgets.ButtonText(bulkReplaceRect, "AT_BulkReplace_Button".Translate()))
            {
                Find.WindowStack.Add(new Dialog_BulkReplace());
            }
            
            // Expand/Collapse All buttons (smaller widths)
            var expandAllRect = new Rect(bulkReplaceRect.xMax + 5f, headerRect.y, 80f, 30f);
            if (Widgets.ButtonText(expandAllRect, "AT_Setting_Editor_ExpandAll".Translate()))
            {
                if (_editorGroupedCache != null)
                {
                    foreach (var modGroup in _editorGroupedCache)
                    {
                        _editorExpandedGroups.Add(modGroup.Key);
                        foreach (var typeGroup in modGroup.Value)
                        {
                            var subGroupKey = $"{modGroup.Key}:{typeGroup.Key}";
                            _editorExpandedSubGroups.Add(subGroupKey);
                        }
                    }
                }
            }
            
            var collapseAllRect = new Rect(expandAllRect.xMax + 5f, headerRect.y, 80f, 30f);
            if (Widgets.ButtonText(collapseAllRect, "AT_Setting_Editor_CollapseAll".Translate()))
            {
                _editorExpandedGroups.Clear();
                _editorExpandedSubGroups.Clear();
            }

            // Update filtered cache first to get accurate count
            UpdateFilteredCache();
            
            // Total label (adjusted position to prevent cutoff)
            var totalRect = new Rect(collapseAllRect.xMax + 10f, headerRect.y, headerRect.width - (collapseAllRect.xMax + 10f - headerRect.x), 30f);
            var prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleLeft;
            int totalCount = 0;
            if (_editorFilteredGroupCache != null)
            {
                totalCount = _editorFilteredGroupCache.Sum(mg => mg.Value.Sum(tg => tg.Value.Count));
            }
            Widgets.Label(totalRect, $"{("AT_Setting_Editor_Total".Translate())}: {totalCount}");
            Text.Anchor = prevAnchor;

            // Column Headers
            DrawEditorColumnHeaders(columnHeaderRect);

            // List View
            DrawEditorAccordionView(contentRect);
        }

        public static void RefreshEditorFilter()
        {
            // Sync TranslatorManager cache to TranslationCacheManager if needed
            if (TranslatorManager.CachedTranslationsV2 != null && TranslatorManager.CachedTranslationsV2.Count > 0)
            {
                foreach (var pair in TranslatorManager.CachedTranslationsV2)
                {
                    if (!TranslationCacheManager.Instance.Cache.ContainsKey(pair.Key))
                    {
                        TranslationCacheManager.AddOrUpdate(pair.Key, pair.Value);
                    }
                }
            }
            
            // Rebuild type cache from InjectionManager data
            BuildTypeCache();
            
            var cache = TranslationCacheManager.Instance.Cache;
            
            // Group by mod and then by type (DefInjected/Keyed) - no search filtering here
            _editorGroupedCache = new Dictionary<string, Dictionary<string, List<KeyValuePair<string, string>>>>();
            
            foreach (var entry in cache)
            {
                var key = entry.Key;
                var modId = "Unknown";
                var translationType = DetermineTranslationType(key);
                
                // Extract ModId from key format: "ModPackageId:originalText"
                var colonIndex = key.IndexOf(':');
                if (colonIndex > 0)
                {
                    var potentialModId = key.Substring(0, colonIndex);
                    if (!string.IsNullOrWhiteSpace(potentialModId))
                    {
                        modId = potentialModId;
                    }
                }
                
                if (!_editorGroupedCache.ContainsKey(modId))
                {
                    _editorGroupedCache[modId] = new Dictionary<string, List<KeyValuePair<string, string>>>();
                }
                
                if (!_editorGroupedCache[modId].ContainsKey(translationType))
                {
                    _editorGroupedCache[modId][translationType] = new List<KeyValuePair<string, string>>();
                }
                
                _editorGroupedCache[modId][translationType].Add(entry);
            }
            
            // Invalidate filtered cache when base cache is rebuilt
            _editorFilteredGroupCache = null;
        }
        
        /// <summary>
        /// Builds a cache mapping original text to translation type (DefInjected/Keyed)
        /// by scanning InjectionManager data once. This avoids repeated O(n) lookups.
        /// </summary>
        private static void BuildTypeCache()
        {
            _typeCache.Clear();
            
            // Scan DefInjected entries
            foreach (var defParam in InjectionManager.defInjectedMissing)
            {
                if (defParam.isCollection)
                {
                    if (defParam.originalCollection != null)
                    {
                        foreach (var original in defParam.originalCollection)
                        {
                            if (!string.IsNullOrEmpty(original))
                            {
                                _typeCache[original] = "DefInjected";
                            }
                        }
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(defParam.original))
                    {
                        _typeCache[defParam.original] = "DefInjected";
                    }
                }
            }
            
            // Scan Keyed entries
            foreach (var keyedParam in InjectionManager.keyedMissing)
            {
                if (!string.IsNullOrEmpty(keyedParam.value.value))
                {
                    _typeCache[keyedParam.value.value] = "Keyed";
                }
            }
        }
        
        /// <summary>
        /// Determines whether a cache key corresponds to a DefInjected or Keyed translation
        /// using the pre-built type cache for O(1) lookup performance.
        /// </summary>
        private static string DetermineTranslationType(string cacheKey)
        {
            // Extract original text from cache key format: "ModPackageId:originalText[+additionalKey]"
            var colonIndex = cacheKey.IndexOf(':');
            string textToSearch;
            
            if (colonIndex > 0 && colonIndex < cacheKey.Length - 1)
            {
                textToSearch = cacheKey.Substring(colonIndex + 1);
            }
            else
            {
                textToSearch = cacheKey;
            }
            
            // Try exact match first (fastest - O(1))
            if (_typeCache.TryGetValue(textToSearch, out var cachedType))
            {
                return cachedType;
            }
            
            // If there's an additionalKey appended, try to find the original text
            // Cache keys may be in format: "originalText+additionalKey"
            foreach (var cachedEntry in _typeCache)
            {
                if (textToSearch.StartsWith(cachedEntry.Key))
                {
                    return cachedEntry.Value;
                }
            }
            
            // Fallback: if not found in cache, try to guess based on structure
            // DefInjected typically has dots (e.g., "ThingDef.Wood.label")
            // Keyed translations usually don't follow this pattern
            if (textToSearch.Contains(".") && textToSearch.Split('.').Length >= 3)
            {
                return "DefInjected";
            }
            
            // Default to Keyed if we can't determine
            return "Keyed";
        }

        private static bool MatchesSearchQuery(KeyValuePair<string, string> entry)
        {
            if (string.IsNullOrEmpty(_editorSearchQuery)) return true;
            
            var query = _editorSearchQuery.ToLower();
            return entry.Key.ToLower().Contains(query) || entry.Value.ToLower().Contains(query) || GetModDisplayName(entry.Key).ToLower().Contains(query);
        }
        
        private static void UpdateFilteredCache()
        {
            if (_editorGroupedCache == null || _editorGroupedCache.Count == 0)
            {
                RefreshEditorFilter();
                
                if (_editorGroupedCache == null || _editorGroupedCache.Count == 0)
                {
                    return;
                }
            }
            
            // Update filtered cache only if not already cached
            if (_editorFilteredGroupCache == null)
            {
                _editorFilteredGroupCache = new Dictionary<string, Dictionary<string, List<KeyValuePair<string, string>>>>();
                
                if (string.IsNullOrEmpty(_editorSearchQuery))
                {
                    // No filtering needed
                    _editorFilteredGroupCache = _editorGroupedCache;
                }
                else
                {
                    // Apply filtering once and cache
                    foreach (var modGroup in _editorGroupedCache)
                    {
                        var filteredMod = new Dictionary<string, List<KeyValuePair<string, string>>>();
                        
                        foreach (var typeGroup in modGroup.Value)
                        {
                            var filtered = typeGroup.Value.Where(MatchesSearchQuery).ToList();
                            if (filtered.Count > 0)
                            {
                                filteredMod[typeGroup.Key] = filtered;
                            }
                        }
                        
                        if (filteredMod.Count > 0)
                        {
                            _editorFilteredGroupCache[modGroup.Key] = filteredMod;
                        }
                    }
                }
            }
        }
        
        private static void DrawEditorAccordionView(Rect contentRect)
        {
            if (_editorFilteredGroupCache == null || _editorFilteredGroupCache.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(contentRect, "No translation data available.");
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }

            float totalHeight = 0f;
            foreach (var modGroup in _editorFilteredGroupCache.OrderBy(g => GetModDisplayName(g.Key)))
            {
                totalHeight += GroupHeaderHeight;
                if (_editorExpandedGroups.Contains(modGroup.Key))
                {
                    foreach (var typeGroup in modGroup.Value.OrderBy(t => t.Key))
                    {
                        totalHeight += SubGroupHeaderHeight;
                        var subGroupKey = $"{modGroup.Key}:{typeGroup.Key}";
                        if (_editorExpandedSubGroups.Contains(subGroupKey))
                        {
                            totalHeight += typeGroup.Value.Count * RowHeight;
                        }
                    }
                }
            }
            
            var viewRect = new Rect(0f, 0f, contentRect.width - 16f, totalHeight);
            Widgets.BeginScrollView(contentRect, ref _editorScrollPosition, viewRect);

            float currentY = 0f;
            float viewTop = _editorScrollPosition.y;
            float viewBottom = _editorScrollPosition.y + contentRect.height;
            
            foreach (var modGroup in _editorFilteredGroupCache.OrderBy(g => GetModDisplayName(g.Key)))
            {
                
                var isModExpanded = _editorExpandedGroups.Contains(modGroup.Key);
                
                var groupHeaderRect = new Rect(0f, currentY, viewRect.width, GroupHeaderHeight);
                
                // Draw mod group header
                var bgColor = Mouse.IsOver(groupHeaderRect) 
                    ? new Color(0.3f, 0.3f, 0.3f, 0.5f) 
                    : new Color(0.2f, 0.2f, 0.2f, 0.5f);
                Widgets.DrawBoxSolid(groupHeaderRect, bgColor);
                Widgets.DrawBox(groupHeaderRect);
                
                var arrowRect = new Rect(groupHeaderRect.x + 5f, groupHeaderRect.y, 20f, GroupHeaderHeight);
                var prevAnchor = Text.Anchor;
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(arrowRect, isModExpanded ? "▼" : "▶");
                Text.Anchor = prevAnchor;
                
                var labelRect = new Rect(arrowRect.xMax + 5f, groupHeaderRect.y, groupHeaderRect.width - 30f, GroupHeaderHeight);
                var prevFont = Text.Font;
                Text.Font = GameFont.Small;
                Text.Anchor = TextAnchor.MiddleLeft;
                var displayName = GetModDisplayName(modGroup.Key);
                var totalCount = modGroup.Value.Sum(t => t.Value.Count);
                Widgets.Label(labelRect, $"{displayName} ({totalCount})");
                Text.Font = prevFont;
                Text.Anchor = prevAnchor;
                
                if (Widgets.ButtonInvisible(groupHeaderRect))
                {
                    if (isModExpanded)
                    {
                        _editorExpandedGroups.Remove(modGroup.Key);
                    }
                    else
                    {
                        _editorExpandedGroups.Add(modGroup.Key);
                    }
                }
                
                currentY += GroupHeaderHeight;
                
                if (isModExpanded)
                {
                    // Draw type subgroups (DefInjected/Keyed)
                    foreach (var typeGroup in modGroup.Value.OrderBy(t => t.Key))
                    {
                        var subGroupKey = $"{modGroup.Key}:{typeGroup.Key}";
                        var isSubGroupExpanded = _editorExpandedSubGroups.Contains(subGroupKey);
                        
                        var subGroupHeaderRect = new Rect(20f, currentY, viewRect.width - 20f, SubGroupHeaderHeight);
                        
                        // Draw subgroup header
                        var subBgColor = Mouse.IsOver(subGroupHeaderRect) 
                            ? new Color(0.25f, 0.25f, 0.3f, 0.4f) 
                            : new Color(0.15f, 0.15f, 0.2f, 0.4f);
                        Widgets.DrawBoxSolid(subGroupHeaderRect, subBgColor);
                        Widgets.DrawBox(subGroupHeaderRect);
                        
                        var subArrowRect = new Rect(subGroupHeaderRect.x + 5f, subGroupHeaderRect.y, 20f, SubGroupHeaderHeight);
                        prevAnchor = Text.Anchor;
                        Text.Anchor = TextAnchor.MiddleCenter;
                        Widgets.Label(subArrowRect, isSubGroupExpanded ? "▼" : "▶");
                        Text.Anchor = prevAnchor;
                        
                        var subLabelRect = new Rect(subArrowRect.xMax + 5f, subGroupHeaderRect.y, subGroupHeaderRect.width - 30f, SubGroupHeaderHeight);
                        prevFont = Text.Font;
                        Text.Font = GameFont.Tiny;
                        Text.Anchor = TextAnchor.MiddleLeft;
                        Widgets.Label(subLabelRect, $"{typeGroup.Key} ({typeGroup.Value.Count})");
                        Text.Font = prevFont;
                        Text.Anchor = prevAnchor;
                        
                        if (Widgets.ButtonInvisible(subGroupHeaderRect))
                        {
                            if (isSubGroupExpanded)
                            {
                                _editorExpandedSubGroups.Remove(subGroupKey);
                            }
                            else
                            {
                                _editorExpandedSubGroups.Add(subGroupKey);
                            }
                        }
                        
                        currentY += SubGroupHeaderHeight;
                        
                        if (isSubGroupExpanded)
                        {
                            // Virtual scrolling - only render visible items
                            for (int i = 0; i < typeGroup.Value.Count; i++)
                            {
                                var rowTop = currentY;
                                var rowBottom = currentY + RowHeight;
                                
                                // Only render if row is in visible area (with small buffer)
                                if (rowBottom >= viewTop - RowHeight && rowTop <= viewBottom + RowHeight)
                                {
                                    var entry = typeGroup.Value[i];
                                    var rowRect = new Rect(40f, currentY, viewRect.width - 40f, RowHeight);
                                    
                                    if (i % 2 == 0) Widgets.DrawLightHighlight(rowRect);
                                    
                                    DrawEditorEntryRow(rowRect, entry);
                                }
                                
                                currentY += RowHeight;
                            }
                        }
                    }
                }
            }

            Widgets.EndScrollView();
        }

        private static void DrawEditorEntryRow(Rect rowRect, KeyValuePair<string, string> entry)
        {
            var keyLanguage = LanguageDetector.Detect(entry.Key);
            var valueLanguage = LanguageDetector.Detect(entry.Value);
            
            var keyRect = new Rect(rowRect.x, rowRect.y, rowRect.width * 0.35f, RowHeight);
            Widgets.Label(keyRect.ContractedBy(2f), entry.Key);
            TooltipHandler.TipRegion(keyRect, $"{entry.Key}\n\n{LanguageDetector.GetLanguageInfo(entry.Key)}");

            var keyLangRect = new Rect(keyRect.xMax, rowRect.y, rowRect.width * 0.08f, RowHeight);
            DrawEditorLanguageBadge(keyLangRect, keyLanguage);

            var valRect = new Rect(keyLangRect.xMax, rowRect.y, rowRect.width * 0.35f, RowHeight);
            
            if (_editingKey == entry.Key)
            {
                _editingValue = Widgets.TextField(valRect.ContractedBy(2f), _editingValue);
            }
            else
            {
                Widgets.Label(valRect.ContractedBy(2f), entry.Value);
                TooltipHandler.TipRegion(valRect, $"{entry.Value}\n\n{LanguageDetector.GetLanguageInfo(entry.Value)}");
            }

            var valLangRect = new Rect(valRect.xMax, rowRect.y, rowRect.width * 0.08f, RowHeight);
            DrawEditorLanguageBadge(valLangRect, valueLanguage);

            var btnRect = new Rect(valLangRect.xMax, rowRect.y, rowRect.width * 0.14f, RowHeight);
            var editBtnRect = new Rect(btnRect.x, btnRect.y, btnRect.width / 2f, RowHeight).ContractedBy(2f);
            var delBtnRect = new Rect(btnRect.x + btnRect.width / 2f, btnRect.y, btnRect.width / 2f, RowHeight).ContractedBy(2f);

            if (_editingKey == entry.Key)
            {
                if (Widgets.ButtonText(editBtnRect, "AT_Setting_Editor_Save".Translate()))
                {
                    string newValue;
                    if (string.IsNullOrWhiteSpace(_editingValue))
                    {
                        var colonIndex = entry.Key.IndexOf(':');
                        var originalText = colonIndex > 0 && colonIndex < 100 
                            ? entry.Key.Substring(colonIndex + 1) 
                            : entry.Key;
                        newValue = originalText;
                    }
                    else
                    {
                        newValue = _editingValue;
                    }
                    
                    // Update file cache and in-memory cache
                    TranslationCacheManager.AddOrUpdate(entry.Key, newValue);
                    TranslatorManager.CachedTranslationsV2[entry.Key] = newValue;
                    
                    // Changes require restart to take effect
                    _restartRequired = true;
                    
                    _editingKey = null;
                    RefreshEditorFilter();
                }
            }
            else
            {
                if (Widgets.ButtonText(editBtnRect, "AT_Setting_Editor_Edit".Translate()))
                {
                    _editingKey = entry.Key;
                    _editingValue = entry.Value;
                }
            }

            if (Widgets.ButtonText(delBtnRect, "AT_Setting_Editor_Delete".Translate()))
            {
                // Remove from file cache and in-memory cache
                TranslationCacheManager.Remove(entry.Key);
                TranslatorManager.CachedTranslationsV2.TryRemove(entry.Key, out _);
                
                // Changes require restart to take effect
                _restartRequired = true;
                
                RefreshEditorFilter();
            }
        }

        private static void DrawEditorColumnHeaders(Rect rect)
        {
            var prevAnchor = Text.Anchor;
            var prevFont = Text.Font;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.MiddleLeft;

            var keyHeaderRect = new Rect(rect.x, rect.y, rect.width * 0.35f, rect.height);
            Widgets.Label(keyHeaderRect.ContractedBy(2f), "AT_Setting_Editor_OriginalText".Translate());
            
            var keyLangHeaderRect = new Rect(keyHeaderRect.xMax, rect.y, rect.width * 0.08f, rect.height);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(keyLangHeaderRect, "AT_Setting_Editor_Language".Translate());
            
            var valHeaderRect = new Rect(keyLangHeaderRect.xMax, rect.y, rect.width * 0.35f, rect.height);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(valHeaderRect.ContractedBy(2f), "AT_Setting_Editor_TranslatedText".Translate());
            
            var valLangHeaderRect = new Rect(valHeaderRect.xMax, rect.y, rect.width * 0.08f, rect.height);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(valLangHeaderRect, "AT_Setting_Editor_Language".Translate());
            
            var actionsHeaderRect = new Rect(valLangHeaderRect.xMax, rect.y, rect.width * 0.14f, rect.height);
            Widgets.Label(actionsHeaderRect.ContractedBy(2f), "AT_Setting_Editor_Actions".Translate());

            Widgets.DrawLineHorizontal(rect.x, rect.yMax - 1f, rect.width);

            Text.Anchor = prevAnchor;
            Text.Font = prevFont;
        }

        private static void DrawEditorLanguageBadge(Rect rect, LanguageDetector.DetectedLanguage language)
        {
            var badgeRect = rect.ContractedBy(2f);
            
            var (langText, tooltip, bgColor) = LanguageDetector.GetLanguageDisplayInfo(language);
            
            Widgets.DrawBoxSolid(badgeRect, bgColor);
            Widgets.DrawBox(badgeRect);
            
            var prevAnchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(badgeRect, langText);
            Text.Anchor = prevAnchor;
            
            TooltipHandler.TipRegion(badgeRect, tooltip);
        }

        private static void ResetDefCaches()
        {
            try
            {
                var defTypes = InjectionManager.defTypesTranslated.ToList();
                Log.Message(AutoTranslation.LogPrefix + $"Resetting Def caches for {defTypes.Count} types...");
                
                foreach (var defType in defTypes)
                {
                    try
                    {
                        GenGeneric.InvokeStaticMethodOnGenericType(typeof(DefDatabase<>), defType, "ClearCachedData");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(AutoTranslation.LogPrefix + $"Failed to clear cache for {defType}: {ex.Message}");
                    }
                }
                
                Log.Message(AutoTranslation.LogPrefix + "Def caches reset complete");
            }
            catch (Exception ex)
            {
                Log.Error(AutoTranslation.LogPrefix + $"Error resetting Def caches: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Gets the display name for a mod by its package ID.
        /// Falls back to the package ID if the mod name cannot be found.
        /// </summary>
        private static string GetModDisplayName(string modPackageId)
        {
            if (string.IsNullOrEmpty(modPackageId))
                return "Unknown";

            if (_editorModNameCache.TryGetValue(modPackageId, out var name))
                return name;

            var mod = AllMods
                .Select(m => new { m.PackageId, m.Name })
                .Concat(ModLister.AllInstalledMods.Select(m => new { m.PackageId, m.Name }))
                .FirstOrDefault(m => m.PackageId.Equals(modPackageId, StringComparison.OrdinalIgnoreCase));

            _editorModNameCache[modPackageId] = mod?.Name ?? modPackageId;
            return _editorModNameCache[modPackageId];
        }
    }
}
