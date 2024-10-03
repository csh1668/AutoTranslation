using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Noise;

namespace AutoTranslation
{
    [HarmonyPatch]
    public static class Patches
    {
        private static bool _defInjectedMissingloaded, _keyedMissingLoaded;

        internal static IEnumerable<Type> defTypesTranslated
        {
            get
            {
                var hashSet = new HashSet<Type>();
                foreach (var @params in InjectionManager.defInjectedMissing)
                {
                    hashSet.Add(@params.defType);
                }
                return hashSet;
            }
        }

        [HarmonyPatch(typeof(LoadedLanguage)), HarmonyPatch(nameof(LoadedLanguage.InjectIntoData_AfterImpliedDefs)), HarmonyPostfix]
        public static void Postfix_LoadedLanguage_InjectIntoData_AfterImpliedDefs()
        {
            if (_defInjectedMissingloaded) return;

            AutoTranslation.sw.Start();
            Log.Message(AutoTranslation.LogPrefix + "finding untranslated DefInjected...");

            // TODO: DELETE THIS
            SpaghettiCodes.SpanishPsychology();


            _defInjectedMissingloaded = true;
            InjectionManager.InjectMissingDefInjection();

            Log.Message(AutoTranslation.LogPrefix + "finding untranslated DefInjected done!");

            AutoTranslation.sw.Stop();
        }

        [HarmonyPatch(typeof(LoadedLanguage)), HarmonyPatch(nameof(LoadedLanguage.InjectIntoData_BeforeImpliedDefs)), HarmonyPostfix]
        public static void Postfix_LoadedLanguage_InjectIntoData_BeforeImpliedDefs()
        {
            if (_keyedMissingLoaded) return;

            AutoTranslation.sw.Start();
            _keyedMissingLoaded = true;
            Log.Message(AutoTranslation.LogPrefix + "finding untranslated Keyed...");

            InjectionManager.InjectMissingKeyed();

            Log.Message(AutoTranslation.LogPrefix + "finding untranslated Keyed done!");
            AutoTranslation.sw.Stop();
        }


        [HarmonyPatch(typeof(GUI)), HarmonyPatch(nameof(GUI.Label)), HarmonyPatch(new[] { typeof(Rect), typeof(string), typeof(GUIStyle) }), HarmonyPostfix]
        public static void Post_GUI_Label(Rect position, string text)
        {
            if (!Settings.ShowOriginal || string.IsNullOrEmpty(text)) return;
            if (InjectionManager.ReverseTranslator.TryGetValue(text, out var value))
            {
                TooltipHandler.TipRegion(position, new TipSignal(value));
            }
        }
    }
}
