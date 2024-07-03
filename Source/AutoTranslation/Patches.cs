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
        internal static readonly ConcurrentBag<DefInjectionUtilityCustom.DefInjectionUntranslatedParams> defInjectedMissing =
            new ConcurrentBag<DefInjectionUtilityCustom.DefInjectionUntranslatedParams>();
        internal static readonly ConcurrentBag<(string, string)> keyedMissing = new ConcurrentBag<(string, string)>();
        internal static bool keyedDone = false;

        internal static readonly ConcurrentDictionary<string, string> ReverseTranslator = new ConcurrentDictionary<string, string>();

        internal static IEnumerable<Type> defTypesTranslated
        {
            get
            {
                var hashSet = new HashSet<Type>();
                foreach (var @params in defInjectedMissing)
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
            DefInjectionUtilityCustom.FindMissingDefInjection((@params =>
            {
                if (@params.field.Name.ToLower().Contains("path")) return;

                defInjectedMissing.Add(@params);
                if (!@params.isCollection)
                {
                    TranslatorManager.Translate(@params.original, t =>
                    {
                        @params.translated = t;
                        @params.InjectIntoDef();

                        if (string.IsNullOrEmpty(t)) return;
                        ReverseTranslator[t] = @params.original;
                    });
                }
                else
                {
                    foreach (var original in @params.originalCollection)
                    {
                        if (original.Contains("->"))
                        {
                            var token = original.Split(new[] { "->" }, StringSplitOptions.None);
                            var key = token[0];
                            var (value, placeHolders) = token[1].ToFormatString();
                            TranslatorManager.Translate(value, key+placeHolders.ToLineList(), t =>
                            {
                                string t2 = string.Empty;
                                try
                                {
                                    t = t.FitFormatCount(placeHolders.Count);
                                    t2 = key + "->" + string.Format(t, placeHolders.ToArray());
                                    if (!@params.translatedCollection.TryAdd(original, t2)) {}
                                }
                                catch (Exception e)
                                {
                                    Log.WarningOnce(AutoTranslation.LogPrefix + $"Formating failed: {key}:{value} => {t}, {placeHolders.Count}, reason {e.Message}", value.GetHashCode());
                                    @params.translatedCollection.TryAdd(original, original);
                                    TranslatorManager.CachedTranslations.TryRemove(value, out _);
                                }
                                @params.InjectIntoDef();

                                if (string.IsNullOrEmpty(t2)) return;
                                ReverseTranslator[t2] = original;
                            });
                        }
                        
                    }
                }
            }));

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

            var bag = new ConcurrentBag<(string, string)>();
            foreach (var valueTuple in KeyedUtility.FindMissingKeyed())
            {
                keyedMissing.Add(valueTuple);
            }

            foreach (var (k, v) in keyedMissing)
            {
                TranslatorManager.Translate(v, k, t =>
                {
                    bag.Add((k, t));
                    ReverseTranslator[t] = v;

                    if (bag.Count == keyedMissing.Count)
                    {
                        keyedDone = true;
                        LongEventHandler.QueueLongEvent(() =>
                        {
                            foreach (var (key, value) in bag)
                            {
                                if (string.IsNullOrEmpty(key)) continue;
                                KeyedUtility.AddKeyedToCurrentLanguage(key, value);
                            }

                            Messages.Message("AT_Message_KeyedDone".Translate(), MessageTypeDefOf.PositiveEvent);
                        }, "AT_addKeyed", false, null);
                    }
                });
            }

            Log.Message(AutoTranslation.LogPrefix + "finding untranslated Keyed done!");
            AutoTranslation.sw.Stop();
        }


        [HarmonyPatch(typeof(GUI)), HarmonyPatch(nameof(GUI.Label)), HarmonyPatch(new[] { typeof(Rect), typeof(string), typeof(GUIStyle) }), HarmonyPostfix]
        public static void Post_GUI_Label(Rect position, string text)
        {
            if (!Settings.ShowOriginal) return;
            if (ReverseTranslator.TryGetValue(text, out var value))
            {
                TooltipHandler.TipRegion(position, new TipSignal(value));
            }
        }
    }
}
