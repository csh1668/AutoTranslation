﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
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

            _defInjectedMissingloaded = true;
            DefInjectionUtilityCustom.FindMissingDefInjection((@params =>
            {
                if (@params.field.Name.ToLower().Contains("path")) return;

                if (!@params.isCollection)
                {
                    defInjectedMissing.Add(@params);
                    TranslatorManager.Translate(@params.original, t =>
                    {
                        @params.translated = t;
                        @params.InjectIntoDef();
                    });
                }
                else
                {
                    defInjectedMissing.Add(@params);
                    foreach (var original in @params.originalCollection)
                    {
                        if (original.Contains("->"))
                        {
                            var token = original.Split(new[] { "->" }, StringSplitOptions.None);
                            var key = token[0];
                            var (value, placeHolders) = token[1].ToFormatString();
                            TranslatorManager.Translate(value, key+placeHolders.ToLineList(), t =>
                            {
                                try
                                {
                                    t = t.FitFormatCount(placeHolders.Count);
                                    var t2 = key + "->" + string.Format(t, placeHolders.ToArray());
                                    if (!@params.translatedCollection.TryAdd(original, t2)) {}
                                }
                                catch (Exception e)
                                {
                                    Log.WarningOnce(AutoTranslation.LogPrefix + $"Formating failed: {key}:{value} => {t}, {placeHolders.Count}, reason {e.Message}", value.GetHashCode());
                                    @params.translatedCollection.TryAdd(original, original);
                                    TranslatorManager.CachedTranslations.TryRemove(value, out _);
                                }
                                @params.InjectIntoDef();
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

                    if (bag.Count == keyedMissing.Count)
                    {
                        keyedDone = true;
                        LongEventHandler.QueueLongEvent(() =>
                        {
                            foreach (var (key, value) in bag)
                            {
                                KeyedUtility.AddKeyedToCurrentLanguage(key, value);
                            }

                            Messages.Message("".Translate(), MessageTypeDefOf.PositiveEvent);
                        }, "AT_addKeyed", false, null);
                    }
                });
            }

            Log.Message(AutoTranslation.LogPrefix + "finding untranslated Keyed done!");
            AutoTranslation.sw.Stop();
        }
    }
}