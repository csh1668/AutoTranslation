using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace AutoTranslation
{
    /*
     * TODO LIST:
     * 1. ruleStrings, ruleFiles는 그냥 번역기에 집어넣으면 안됨.
     * 예시 ruleStrings: maybe_adjective->[Color]의 경우 ->를 기준으로 쪼개고, 오른쪽만 번역을 돌림. 이때 [] 안에 있는 문자열은 {0} 등의 문자열로 임시로 빼놨다가...
     * 예시 ruleFiles: businessname->Names/Business의 경우 얘는 그냥 번역하면 안됨. 이후에 ruleFiles가 파싱될 때 그때 번역 해야함.
     * 2. Keyed, Strings 번역
     */
    public class AutoTranslation : Mod
    {
        internal static readonly Stopwatch sw = new Stopwatch();
        internal const string LogPrefix = "<color=#34e2eb>AutoTranslation</color>: ";

        private Settings settings;

        public AutoTranslation(ModContentPack content) : base(content)
        {
            settings = GetSettings<Settings>();

            var h = new Harmony("seohyeon.autotranslation");
            h.PatchAll();
            Log.Message(LogPrefix + "Harmony patches are applied!");

            TranslatorManager.Prepare();
            TranslatorManager.StartThread();
        }

        public override string SettingsCategory() => "AT_Name".Translate();

        public override void DoSettingsWindowContents(Rect inRect)
        {
            base.DoSettingsWindowContents(inRect);

            settings.DoSettingsWindowContents(inRect);
        }
    }
}
