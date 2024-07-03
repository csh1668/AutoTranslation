using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace AutoTranslation
{
    public class Settings : ModSettings
    {
        public static bool AppendTranslationCompleteTag = false;
        public static string APIKey = string.Empty;
        public static string TranslatorName = "Google";
        public static bool ShowOriginal = false;

        internal static bool RequiresTokenKey = false;
        

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref AppendTranslationCompleteTag, "AutoTranslation_AppendTranslationCompleteTag", false);
            Scribe_Values.Look(ref APIKey, "AutoTranslation_APIKey", string.Empty);
            Scribe_Values.Look(ref TranslatorName, "AutoTranslation_TranslatorName", "Google");
            Scribe_Values.Look(ref ShowOriginal, "AutoTranslation_ShowOriginal", false);
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
            DoSettingsWindowContentsRight(inRect);
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

        public void DoSettingsWindowContentsRight(Rect inRect)
        {
            var ls = new Listing_Standard();
            ls.Begin(inRect);

            ls.Label("AT_Setting_Misc".Translate());
            ls.GapLine();

            if (ls.ButtonText("AT_Setting_ResetDefCache".Translate()))
            {
                foreach (var defType in Patches.defTypesTranslated)
                {
                    GenGeneric.InvokeStaticMethodOnGenericType(typeof(DefDatabase<>), defType, "ClearCachedData");
                }
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
            ls.Label("AT_Setting_NotYet".Translate()+$"{TranslatorManager._queue.Count}");

            if (ls.ButtonText("AT_Setting_OpenDir".Translate()))
            {
                Application.OpenURL($"file://{CacheFileTool.CacheDirectory}");
            }

            ls.End();
        }
        
    }
}
