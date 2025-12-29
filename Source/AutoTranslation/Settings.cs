using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoTranslation.Translators;
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
        public static string APIKey = string.Empty;
        public static string TranslatorName = "Google";
        public static bool ShowOriginal = false;
        public static HashSet<string> BlackListModPackageIds = new HashSet<string>();
        public static int SleepTime = 0;
        public static int ConcurrentWorkerCount = 1;

        public static string SelectedModel = string.Empty;
        public static string CustomBaseURL = string.Empty;
        public static string CustomPrompt = string.Empty;

        private static List<ModContentPack> AllMods => _allModsCached ?? (_allModsCached = LoadedModManager.RunningMods.ToList());
        private static List<ModContentPack> _allModsCached;
        private static Vector2 scrollbarVector = Vector2.zero;
        private static string TestText = "Hello, World!";
        private static string TestResultText = string.Empty;
        private static string SearchText = string.Empty;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref AppendTranslationCompleteTag, "AutoTranslation_AppendTranslationCompleteTag", false);
            Scribe_Values.Look(ref APIKey, "AutoTranslation_APIKey", string.Empty);
            Scribe_Values.Look(ref TranslatorName, "AutoTranslation_TranslatorName", "Google");
            Scribe_Values.Look(ref ShowOriginal, "AutoTranslation_ShowOriginal", false);
            Scribe_Values.Look(ref SelectedModel, "AutoTranslation_SelectedModel", string.Empty);
            Scribe_Values.Look(ref CustomBaseURL, "AutoTranslation_CustomBaseURL", string.Empty);
            Scribe_Values.Look(ref CustomPrompt, "AutoTranslation_CustomPrompt", string.Empty);
            Scribe_Values.Look(ref SleepTime, "AutoTranslation_SleepTime", 0);
            Scribe_Values.Look(ref ConcurrentWorkerCount, "AutoTranslation_WorkerCount", 1);

            Scribe_Collections.Look(ref BlackListModPackageIds, "AutoTranslation_WhiteListModPackageIds", LookMode.Value);
            if (BlackListModPackageIds == null) BlackListModPackageIds = new HashSet<string>();
        }

        public void DoSettingsWindowContents(Rect inRect)
        {
            var ls = new Listing_Standard();
            ls.Begin(inRect);

            ls.CheckboxLabeled("AT_Setting_ShowOriginal".Translate(), ref ShowOriginal, "AT_Setting_ShowOriginal_Tooltip".Translate());

            var h = ls.CurHeight;
            ls.End();

            inRect.y += h;
            inRect.height -= h;
            inRect.width /= 2;
            h = DoSettingsWindowContentsLeft(inRect);

            inRect.x += inRect.width;
            h = Mathf.Max(h, DoSettingsWindowContentsRight(inRect));
            inRect.y += h;
            inRect.height -= h;
            inRect.x -= inRect.width;
            inRect.width *= 2;

            const float entryHeight = 22f;
            var cntEntry = AllMods.Count;

            var outRect = new Rect(0f, inRect.y + 20f, inRect.width - 10f, inRect.height - 20f);
            var listRect = new Rect(0f, 0f, outRect.width - 50f, entryHeight * cntEntry);
            var labelRect = new Rect(entryHeight + 10f, 0f, listRect.width - 40f - 70f, entryHeight); // 버튼을 위한 공간 확보
            var retranslateButtonRect = new Rect(labelRect.x + labelRect.width, 0f, 70f, entryHeight);

            var descRect = new Rect(labelRect.x, outRect.y - 22f, labelRect.width, 22f);
            var toggleRect =
                new Rect(labelRect.x + labelRect.width - "AT_Setting_ToggleAll".Translate().GetWidthCached(),
                    outRect.y - 22f, "AT_Setting_ToggleAll".Translate().GetWidthCached(), 22f);
            var searchRect = new Rect(
                labelRect.x + labelRect.width - "AT_Setting_ToggleAll".Translate().GetWidthCached() - 150f,
                outRect.y - 22f, 150f, 22f);
            Widgets.Label(descRect, "AT_Setting_WhiteList".Translate());
            if (Widgets.ButtonText(toggleRect, "AT_Setting_ToggleAll".Translate()))
            {
                if (BlackListModPackageIds.Count == AllMods.Count)
                {
                    BlackListModPackageIds.Clear();
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                }
                else if (BlackListModPackageIds.Count == 0)
                {
                    foreach (var mod in AllMods)
                    {
                        BlackListModPackageIds.Add(mod.PackageId);
                    }
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                }
                else
                {
                    int threshold = AllMods.Count / 2;
                    if (BlackListModPackageIds.Count < threshold)
                    {
                        foreach (var mod in AllMods)
                        {
                            BlackListModPackageIds.Add(mod.PackageId);
                        }
                        SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                    }
                    else
                    {
                        BlackListModPackageIds.Clear();
                        SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                    }
                }
            }
            SearchText = Widgets.TextField(searchRect, SearchText);

            Widgets.BeginScrollView(outRect, ref scrollbarVector, listRect, true);

            var filteredMods = AllMods.Where(m =>
                    string.IsNullOrEmpty(SearchText) || m.Name.ToLower().Contains(SearchText.ToLower()) ||
                    m.PackageId.ToLower().Contains(SearchText.ToLower()))
                .ToList();

            // 번역 통계 가져오기
            var translationStats = InjectionManager.GetTranslationStatsByPackageId();

            for (int i = 0; i < filteredMods.Count; i++)
            {
                var curMod = filteredMods[i];
                var entryRect = new Rect(0f, i * entryHeight, inRect.width - 60f, entryHeight);
                if (i % 2 == 0)
                {
                    Widgets.DrawLightHighlight(entryRect);
                }
                GUI.BeginGroup(entryRect);
#if RW14
#else
                Widgets.ButtonImage(new Rect(0f, 0f, entryHeight, entryHeight), curMod.ModMetaData?.Icon ?? BaseContent.BadTex);
#endif

                // 통계 정보 가져오기
                translationStats.TryGetValue(curMod.PackageId ?? "", out var stats);
                int completedDefs = stats.Item1;
                int completedKeyed = stats.Item2;
                int totalDefs = stats.Item3;
                int totalKeyed = stats.Item4;

                // 완료된 번역 / 총 번역 형식으로 표시
                string statText = $"{completedDefs}/{totalDefs} + {completedKeyed}/{totalKeyed}";

                var tmp = !BlackListModPackageIds.Contains(curMod.PackageId);
                var tmp2 = tmp;
                Widgets.CheckboxLabeled(labelRect, $"{curMod.Name}:::{curMod.PackageId}:::{statText}", ref tmp);
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
                    else if (BlackListModPackageIds.Remove(curMod.PackageId))
                    {
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

                // 재번역 버튼 추가
                if (BlackListModPackageIds.Contains(curMod.PackageId))
                {
                    var curRetranslateRect = new Rect(retranslateButtonRect.x, 0f, retranslateButtonRect.width, retranslateButtonRect.height);
                    if (Widgets.ButtonText(curRetranslateRect, "AT_Setting_Retranslate".Translate()))
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

        public float DoSettingsWindowContentsLeft(Rect inRect)
        {
            var ls = new Listing_Standard();
            ls.Begin(inRect);

            ls.Label("AT_Setting".Translate());
            ls.Label("AT_Setting_Note".Translate());
            ls.GapLine();
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

            var targetTranslator = TranslatorManager.GetTranslator(TranslatorName);

            if (targetTranslator == null)
            {
                ls.Label("AT_Setting_NoTranslatorError".Translate());
                ls.End();
                return ls.CurHeight;
            }

            if (targetTranslator.RequiresKey)
            {
                ls.Label("AT_Setting_RequiresAPIKey".Translate(), tooltip: "AT_Setting_RequiresAPIKey_Tooltip".Translate());
                var textRect = ls.GetRect(Text.LineHeight);
                APIKey = Widgets.TextEntryLabeled(textRect, "API Key:", APIKey);
            }

            if (targetTranslator is Translator_BaseAIModel aiTranslator)
            {
                ls.Label("AT_Setting_BaseURL".Translate() + aiTranslator.BaseURL);
                var textRect = ls.GetRect(Text.LineHeight);
                CustomBaseURL = Widgets.TextEntryLabeled(textRect, "AT_Setting_CustomBaseURL".Translate(), CustomBaseURL);

                if (Widgets.ButtonText(ls.GetRect(28f), "AT_Setting_SelectModel".Translate() + (string.IsNullOrEmpty(SelectedModel) ? (string)"AT_Setting_SelectModelNone".Translate() : SelectedModel)))
                {
                    aiTranslator.ResetSettings();

                    var list = aiTranslator.Models?.Select(m =>
                        new FloatMenuOption(
                            m,
                            () => { SelectedModel = m; })).ToList();

                    if (list == null)
                    {
                        list = new List<FloatMenuOption>
                        {
                            new FloatMenuOption("AT_Setting_NoModelFound".Translate(), () => { }, playSelectionSound: false),
                        };
                    }

                    Find.WindowStack.Add(new FloatMenu(list));
                }

                CustomPrompt = Widgets.TextEntryLabeled(ls.GetRect(Text.LineHeight * 3),
                    "AT_Setting_CustomPrompt".Translate(), CustomPrompt);
            }

            var entryRect = ls.GetRect(28f);

            var left = entryRect.LeftPart(0.33f);
            var mid = new Rect(entryRect.x + left.width, entryRect.y, left.width, entryRect.height);
            var right = new Rect(entryRect.x + left.width + mid.width, entryRect.y, left.width, entryRect.height);

            TestText = Widgets.TextField(left, TestText);

            if (Widgets.ButtonText(mid, "AT_Setting_TestTranslation".Translate()))
            {
                if (targetTranslator is Translator_BaseAIModel ait)
                {
                    ait.ResetSettings();
                }
                // Use the skipRetry parameter for testing to avoid hanging the UI
                var s = targetTranslator.TryTranslate(TestText, out TestResultText, true);
                if (!s)
                {
                    Messages.Message("AT_Message_TestFailed".Translate(), MessageTypeDefOf.NegativeEvent);
                    Log.TryOpenLogWindow();
                }
            }

            Widgets.TextField(right, TestResultText);


            if (Prefs.DevMode)
            {
                ls.CheckboxLabeled("AT_Setting_Test".Translate(), ref AppendTranslationCompleteTag);
            }

            ls.End();

            return ls.CurHeight;
        }

        public float DoSettingsWindowContentsRight(Rect inRect)
        {
            var ls = new Listing_Standard();
            ls.Begin(inRect);

            ls.Label("AT_Setting_Misc".Translate());
            ls.GapLine();

            SleepTime = (int)ls.SliderLabeled("AT_Setting_SleepTime".Translate(SleepTime), SleepTime, 0, 5000);

            ConcurrentWorkerCount = (int)ls.SliderLabeled("AT_Setting_WorkerCount".Translate(ConcurrentWorkerCount), ConcurrentWorkerCount, 1, 5);

            if (ls.ButtonText("AT_Setting_ResetDefCache".Translate()))
            {
                ResetDefCaches();
                Messages.Message("AT_Message_ResetDefCache".Translate(), MessageTypeDefOf.PositiveEvent);
            }

            ls.GapLine();
            string status;
            if (TranslatorManager._queue.Count > 0)
                status = "AT_Status1".Translate();
            else if (TranslatorManager.workCnt > 20) 
                status = "AT_Status2".Translate();
            else 
                status = "AT_Status3".Translate();
            ls.Label("AT_Setting_CurStatus".Translate() + status);
            ls.Label("AT_Setting_Cached".Translate() + $"{TranslatorManager.CachedTranslations.Count}");
            ls.Label("AT_Setting_NotYet".Translate() + $"{TranslatorManager._queue.Count}");

            if (ls.ButtonText("AT_Setting_ResetTranslationCache".Translate()))
            {
                TranslatorManager.CachedTranslations.Clear();
                TranslatorManager._cacheCount = 0;
                CacheFileTool.Export(nameof(TranslatorManager.CachedTranslations), new Dictionary<string, string>(TranslatorManager.CachedTranslations));

                Messages.Message("AT_Message_ResetTranslationCache".Translate(), MessageTypeDefOf.NeutralEvent);
            }

            if (ls.ButtonText("AT_Setting_RestartWork".Translate()))
            {
                var t = TranslatorManager.GetTranslator(TranslatorName);
                if (t is Translator_BaseAIModel aiTranslator)
                {
                    aiTranslator.ResetSettings();
                }
                TranslatorManager.ClearQueue();
                TranslatorManager.CurrentTranslator = t;

                InjectionManager.UndoInjectAll();
                InjectionManager.ClearDefInjectedTranslations();
                InjectionManager.ReverseTranslator.Clear();

                InjectionManager.InjectAll();

                Messages.Message("AT_Message_RestartWork".Translate(), MessageTypeDefOf.NeutralEvent);
            }

            if (ls.ButtonText("AT_Setting_OpenDir".Translate()))
            {
                Application.OpenURL($"file://{CacheFileTool.CacheDirectory}");
            }

            ls.End();

            return ls.CurHeight;
        }

        private static void ResetDefCaches()
        {
            foreach (var defType in InjectionManager.defTypesTranslated)
            {
                GenGeneric.InvokeStaticMethodOnGenericType(typeof(DefDatabase<>), defType, "ClearCachedData");
            }
        }
    }
}
