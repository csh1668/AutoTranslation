using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
        public static HashSet<string> WhiteListModPackageIds = new HashSet<string>();
             
        internal static bool RequiresTokenKey = false;

        private static List<ModContentPack> AllMods => _allModsCached ?? (_allModsCached = LoadedModManager.RunningMods.ToList());
        private static List<ModContentPack> _allModsCached;
        private static Vector2 scrollbarVector = Vector2.zero;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref AppendTranslationCompleteTag, "AutoTranslation_AppendTranslationCompleteTag", false);
            Scribe_Values.Look(ref APIKey, "AutoTranslation_APIKey", string.Empty);
            Scribe_Values.Look(ref TranslatorName, "AutoTranslation_TranslatorName", "Google");
            Scribe_Values.Look(ref ShowOriginal, "AutoTranslation_ShowOriginal", false);
            Scribe_Collections.Look(ref WhiteListModPackageIds, "AutoTranslation_WhiteListModPackageIds", LookMode.Value);
            if (WhiteListModPackageIds == null) WhiteListModPackageIds = new HashSet<string>();
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
            DoSettingsWindowContentsLeft(inRect);

            inRect.x += inRect.width;
            h = DoSettingsWindowContentsRight(inRect);
            inRect.y += h;
            inRect.height -= h;
            inRect.x -= inRect.width;
            inRect.width *= 2;

            const float entryHeight = 22f;
            var cntEntry = AllMods.Count;

            var outRect = new Rect(0f, inRect.y + 20f, inRect.width - 10f, inRect.height - 20f);
            var listRect = new Rect(0f, 0f, outRect.width - 50f, entryHeight * cntEntry);
            var labelRect = new Rect(entryHeight + 10f, 0f, listRect.width - 40f, entryHeight);

            Widgets.Label(new Rect(labelRect.x, outRect.y - 22f, labelRect.width, 22f), "AT_Setting_WhiteList".Translate());
            if (Widgets.ButtonText(new Rect(labelRect.x + labelRect.width - "AT_Setting_ToggleAll".Translate().GetWidthCached(), outRect.y - 22f, "AT_Setting_ToggleAll".Translate().GetWidthCached(), 22f), "AT_Setting_ToggleAll".Translate()))
            {
                if (WhiteListModPackageIds.Count == AllMods.Count)
                {
                    WhiteListModPackageIds.Clear();
                    SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                }
                else if (WhiteListModPackageIds.Count == 0)
                {
                    foreach (var mod in AllMods)
                    {
                        WhiteListModPackageIds.Add(mod.PackageId);
                    }
                    SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                }
                else
                {
                    int threshold = AllMods.Count / 2;
                    if (WhiteListModPackageIds.Count < threshold)
                    {
                        foreach (var mod in AllMods)
                        {
                            WhiteListModPackageIds.Add(mod.PackageId);
                        }
                        SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera();
                    }
                    else
                    {
                        WhiteListModPackageIds.Clear();
                        SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera();
                    }
                }
                

            }
            Widgets.BeginScrollView(outRect, ref scrollbarVector, listRect, true);

            for (int i = 0; i < AllMods.Count; i++)
            {
                var curMod = AllMods[i];
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
                InjectionManager.DefInjectedMissingCountByPackageId.TryGetValue(curMod.ModMetaData?.PackageId ?? "", out var cnt);
                

                var tmp = !WhiteListModPackageIds.Contains(curMod.PackageId);
                var tmp2 = tmp;
                Widgets.CheckboxLabeled(labelRect, $"{curMod.Name}:::{curMod.PackageId}:::{cnt}", ref tmp);
                if (tmp != tmp2)
                {
                    if (!tmp)
                    {
                        WhiteListModPackageIds.Add(curMod.PackageId);
                        if (TranslatorManager._queue.Count == 0)
                        {
                            InjectionManager.UndoInjectMissingDefInjection(curMod);
                            ResetDefCaches();
                        }
                        else
                        {
                            Messages.Message("AT_Message_WhiteList_Failed".Translate(), MessageTypeDefOf.NegativeEvent);
                        }
                    }
                    else if (WhiteListModPackageIds.Remove(curMod.PackageId))
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
                
                GUI.EndGroup();
            }

            Widgets.EndScrollView();
        }

        public void DoSettingsWindowContentsLeft(Rect inRect)
        {
            var ls = new Listing_Standard();
            ls.Begin(inRect);

            ls.Label("AT_Setting".Translate());
            ls.Label("AT_Setting_Note".Translate());
            ls.GapLine();
            ls.Label("AT_Setting_SelectEngine".Translate());
            if(Widgets.ButtonText(ls.GetRect(28f), TranslatorName))
            {
                var list = TranslatorManager.translators.Select(t =>
                    new FloatMenuOption(
                        t.Name,
                        () =>
                        {
                            TranslatorName = t.Name;
                            RequiresTokenKey = t.RequiresKey;
                        })).ToList();
                Find.WindowStack.Add(new FloatMenu(list));
            }

            if (RequiresTokenKey)
            {
                ls.Label("AT_Setting_RequiresAPIKey".Translate());
                var textRect = ls.GetRect(Text.LineHeight);
                APIKey = Widgets.TextEntryLabeled(textRect, "API Key:", APIKey);
            }

            if (Prefs.DevMode)
            {
                ls.CheckboxLabeled("AT_Setting_Test".Translate(), ref AppendTranslationCompleteTag);
            }

            ls.End();
        }

        public float DoSettingsWindowContentsRight(Rect inRect)
        {
            var ls = new Listing_Standard();
            ls.Begin(inRect);

            ls.Label("AT_Setting_Misc".Translate());
            ls.GapLine();

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

            if (ls.ButtonText("AT_Setting_OpenDir".Translate()))
            {
                Application.OpenURL($"file://{CacheFileTool.CacheDirectory}");
            }

            ls.End();

            return ls.CurHeight;
        }

        private static void ResetDefCaches()
        {
            foreach (var defType in Patches.defTypesTranslated)
            {
                GenGeneric.InvokeStaticMethodOnGenericType(typeof(DefDatabase<>), defType, "ClearCachedData");
            }
        }
    }
}
