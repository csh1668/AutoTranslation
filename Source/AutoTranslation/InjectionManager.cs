using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace AutoTranslation
{
    public static class InjectionManager
    {
        internal static readonly ConcurrentBag<DefInjectionUtilityCustom.DefInjectionUntranslatedParams> defInjectedMissing =
            new ConcurrentBag<DefInjectionUtilityCustom.DefInjectionUntranslatedParams>();

        internal static readonly ConcurrentBag<(string, string)> keyedMissing = new ConcurrentBag<(string, string)>();
        internal static readonly ConcurrentDictionary<string, string> ReverseTranslator = new ConcurrentDictionary<string, string>();
        internal static readonly ConcurrentBag<(string, string)> keyedTranslated = new ConcurrentBag<(string, string)>();

        internal static Dictionary<string, int> DefInjectedMissingCountByPackageId
        {
            get
            {
                if (_defInjectedMissingCountByPackageId != null) return _defInjectedMissingCountByPackageId;
                var dict = new Dictionary<string, int>();
                foreach (var id in defInjectedMissing.Select(x => x.def?.modContentPack?.PackageId ?? string.Empty))
                {
                    if (!dict.ContainsKey(id)) dict[id] = 1;
                    else dict[id]++;
                }
                _defInjectedMissingCountByPackageId = dict;
                return _defInjectedMissingCountByPackageId;
            }
        }
        private static Dictionary<string, int> _defInjectedMissingCountByPackageId;

        internal static void InjectMissingDefInjection()
        {
            //if (LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage) return;
            DefInjectionUtilityCustom.FindMissingDefInjection((@params =>
            {
                if (@params.field.Name.ToLower().Contains("path")) return;

                defInjectedMissing.Add(@params);
                InjectMissingDefInjection(@params);
            }));
        }

        internal static void InjectMissingDefInjection(ModContentPack targetMod)
        {
            if (defInjectedMissing.Count == 0) return;
            foreach (var param in defInjectedMissing.Where(x => x.def.modContentPack == targetMod))
            {
                InjectMissingDefInjection(param);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void InjectMissingDefInjection(DefInjectionUtilityCustom.DefInjectionUntranslatedParams @params)
        {
            if (!string.IsNullOrEmpty(@params.def?.modContentPack?.PackageId) &&
                    Settings.WhiteListModPackageIds.Contains(@params.def?.modContentPack?.PackageId)) return;

            if (!@params.isCollection)
            {
                if (@params.translated != null)
                {
                    @params.InjectTranslation();
                    return;
                }
                TranslatorManager.Translate(@params.original, t =>
                {
                    @params.translated = t;
                    @params.InjectTranslation();

                    if (string.IsNullOrEmpty(t)) return;
                    ReverseTranslator[t] = @params.original;
                });
            }
            else
            {
                if (@params.originalCollection.Count == @params.translatedCollection.Count)
                {
                    @params.InjectTranslation();
                    return;
                }
                foreach (var original in @params.originalCollection)
                {
                    if (original.Contains("->"))
                    {
                        var token = original.Split(new[] { "->" }, StringSplitOptions.None);
                        var key = token[0];
                        var (value, placeHolders) = token[1].ToFormatString();
                        TranslatorManager.Translate(value, key + placeHolders.ToLineList(), t =>
                        {
                            string t2 = string.Empty;
                            try
                            {
                                t = t.FitFormat(placeHolders.Count);
                                t2 = key + "->" + string.Format(t, placeHolders.ToArray());
                                if (!@params.translatedCollection.TryAdd(original, t2))
                                {
                                }
                            }
                            catch (Exception e)
                            {
                                Log.WarningOnce(
                                    AutoTranslation.LogPrefix +
                                    $"Formating failed: {key}:{value} => {t}, {placeHolders.Count}, reason {e.Message}",
                                    value.GetHashCode());
                                @params.translatedCollection.TryAdd(original, original);
                                TranslatorManager.CachedTranslations.TryRemove(value, out _);
                            }

                            @params.InjectTranslation();

                            if (string.IsNullOrEmpty(t2)) return;
                            ReverseTranslator[t2] = original;
                        });
                    }
                }
            }
        }

        internal static void InjectMissingKeyed()
        {
            if (LanguageDatabase.activeLanguage == LanguageDatabase.defaultLanguage) return;

            if (keyedMissing.Count == 0)
            {
                foreach (var valueTuple in KeyedUtility.FindMissingKeyed())
                {
                    keyedMissing.Add(valueTuple);
                }
            }

            foreach (var (k, v) in keyedMissing)
            {
                TranslatorManager.Translate(v, k, t =>
                {
                    keyedTranslated.Add((k, t));
                    ReverseTranslator[t] = v;

                    if (keyedTranslated.Count == keyedMissing.Count)
                    {
                        LongEventHandler.QueueLongEvent(() =>
                        {
                            foreach (var (key, value) in keyedTranslated)
                            {
                                if (string.IsNullOrEmpty(key)) continue;
                                KeyedUtility.AddKeyedToCurrentLanguage(key, value);
                            }

                            Messages.Message("AT_Message_KeyedDone".Translate(), MessageTypeDefOf.PositiveEvent);
                        }, "AT_addKeyed", false, null);
                    }
                });
            }
        }

        internal static void UndoInjectMissingDefInjection()
        {
            foreach (var param in defInjectedMissing)
            {
                param.UndoInject();
            }
        }

        internal static void UndoInjectMissingDefInjection(ModContentPack targetMod)
        {
            foreach (var param in defInjectedMissing.Where(x => x.def.modContentPack == targetMod))
            {
                param.UndoInject();
            }
        }

        internal static void UndoInjectMissingKeyed()
        {
            foreach (var (k, _) in keyedMissing)
            {
                KeyedUtility.RemoveKeyedFromCurrentLanguage(k);
            }
        }
    }
}
