using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;
using static Verse.DefInjectionPackage;

namespace AutoTranslation
{
    /// <summary>
    /// Similar to Verse.DefInjectionUtility but much faster. (60000ms -> 3000ms)
    /// </summary>
    public static class DefInjectionUtilityCustom
    {
        public delegate void Traverser(string normalizedPath, string suggestedPath, bool isCollection, string curValue,
            IEnumerable<string> curValueEnumerable, object parentObject, FieldInfo fi, Def def);
        private static readonly Dictionary<Type, List<FieldInfo>> fieldsCached = new Dictionary<Type, List<FieldInfo>>();

        public static void FindMissingDefInjection(Action<DefInjectionUntranslatedParams> callBack)
        {
            AddBlackList();

            var injectionsByNormalizedPath = new Dictionary<string, DefInjection>();
            foreach (var (key, value) in LanguageDatabase.activeLanguage.defInjections.SelectMany(x => x.injections))
            {
                if (!injectionsByNormalizedPath.ContainsKey(value.normalizedPath))
                    injectionsByNormalizedPath.Add(value.normalizedPath, value);
            }
            foreach (var defInjectionPackage in LanguageDatabase.activeLanguage.defInjections
                         .Where(x => !blackListTypes.Any(black => x.defType.IsAssignableFrom(black))).OrderBy(x => Order(x.defType)))
            {
                ForEachPossibleDefInjection(defInjectionPackage.defType,
                    (normalizedPath, suggestedPath, isCollection, value, enumerableValue, parentObject, fi, def) =>
                    {
                        if (!isCollection)
                        {

                            bool flag = false;
                            if (injectionsByNormalizedPath.TryGetValue(normalizedPath, out var defInjection) && !defInjection.IsFullListInjection)
                            {
                                if (defInjection.isPlaceholder)
                                {
                                    flag = true;
                                }
                            }
                            else
                            {
                                flag = true;
                            }

                            if (flag && DefInjectionUtility.ShouldCheckMissingInjection(value, fi, def))
                            {
                                callBack(new DefInjectionUntranslatedParams(normalizedPath, suggestedPath, value,
                                    parentObject, fi, def));
                            }
                        }
                        else if (injectionsByNormalizedPath.TryGetValue(normalizedPath, out var defInjection) && defInjection.IsFullListInjection)
                        {
                            if (defInjection.isPlaceholder && !def.generated)
                            {
                                //Log.Message($"fulllist: {def.defName}::{normalizedPath}::{enumerableValue?.Count()}");
                            }
                        }
                        else
                        {
                            if (normalizedPath.Contains("rulesFiles"))
                                return;
                            int num = 0; bool flag = false;
                            var lst = enumerableValue.ToList();
                            foreach (var element in lst)
                            {
                                var key = normalizedPath + "." + num;
                                var curSuggestedPath = suggestedPath + "." + num;

                                if (injectionsByNormalizedPath.TryGetValue(key, out var defInjection2) && !defInjection2.IsFullListInjection)
                                {
                                    if (defInjection2.isPlaceholder)
                                        flag = true;
                                }
                                else flag = true;

                                if (flag && DefInjectionUtility.ShouldCheckMissingInjection(element, fi, def))
                                {
                                    callBack(new DefInjectionUntranslatedParams(normalizedPath, suggestedPath, lst, parentObject, fi, def));
                                }

                                num++;
                            }
                        }
                    });
            }
            
        }

        public static void ForEachPossibleDefInjection(Type defType, Traverser action)
        {
            if (!defType.IsSubclassOf(typeof(Def)))
            {
                Log.Message(AutoTranslation.LogPrefix + $"Type {defType.Name} isn't subclass of Def");
                return;
            }
            foreach (var def in GenDefDatabase.GetAllDefsInDatabaseForDef(defType))
            {
                ForEachPossibleDefInjectionInDef(def, action);
            }
        }

        private static void ForEachPossibleDefInjectionInDef(Def def, Traverser action)
        {
            var visited = new HashSet<object>();
            ForEachPossibleDefInjectionInDefRecursive(def, def.defName, def.defName, visited, def, action);
        }

        private static void ForEachPossibleDefInjectionInDefRecursive(object cur, string curNormalizedPath, string curSuggestedPath, HashSet<object> visited, Def def, Traverser action)
        {
            if (cur == null || cur is Thing || !cur.GetType().IsValueType && visited.Contains(cur))
                return;
            visited.Add(cur);
            foreach (var field in GetFieldsOptimized(cur.GetType()))
            {
                if (blackListFields.Any(x => field.Name == x)) continue;

                var nxt = field.GetValue(cur);
                if (nxt is Def) continue;

                // String or TaggedString일 경우
                if (typeof(string).IsAssignableFrom(field.FieldType))
                {
                    var nxtNormalizedPath = curNormalizedPath + "." + field.Name;
                    if (!TKeySystem.TrySuggestTKeyPath(nxtNormalizedPath, out var nxtSuggestedPath))
                        nxtSuggestedPath = curSuggestedPath + "." + field.Name;
                    action(nxtNormalizedPath, nxtSuggestedPath, false, (string)nxt, null, cur, field, def);
                }
                else if (nxt is IEnumerable<string> nxtStringCollection)
                {
                    var nxtNormalizedPath = curNormalizedPath + "." + field.Name;
                    if (!TKeySystem.TrySuggestTKeyPath(nxtNormalizedPath, out var nxtSuggestedPath))
                        nxtSuggestedPath = curSuggestedPath + "." + field.Name;
                    action(nxtNormalizedPath, nxtSuggestedPath, true, null, nxtStringCollection, cur, field, def);
                }
                else if (nxt is IEnumerable nxtCollection)
                {
                    int idx = 0;
                    foreach (var item in nxtCollection)
                    {
                        if (item != null && !(item is Def) && GenTypes.IsCustomType(item.GetType()))
                        {
                            var handle = TranslationHandleUtility.GetBestHandleWithIndexForListElement(nxtCollection, item);
                            if (string.IsNullOrEmpty(handle))
                                handle = idx.ToString();
                            var nxtNormalizedPath = $"{curNormalizedPath}.{field.Name}.{idx}";
                            var nxtSuggestedPath = $"{curSuggestedPath}.{field.Name}.{handle}";
                            ForEachPossibleDefInjectionInDefRecursive(item, nxtNormalizedPath, nxtSuggestedPath, visited, def, action);
                        }

                        idx++;
                    }
                }
                else if (nxt != null && GenTypes.IsCustomType(nxt.GetType()))
                {
                    ForEachPossibleDefInjectionInDefRecursive(nxt, curNormalizedPath + "." + field.Name,
                        curSuggestedPath + "." + field.Name, visited, def, action);
                }
            }
        }

        private static List<FieldInfo> GetFieldsOptimized(Type type)
        {
            if (fieldsCached.TryGetValue(type, out var fields)) return fields;
            fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(field => !field.HasAttribute<UnsavedAttribute>() && !field.HasAttribute<NoTranslateAttribute>())
                .OrderByDescending(field => field.Name == "label")
                .ThenByDescending(field => field.Name == "description").ToList();
            fieldsCached.Add(type, fields);
            return fields;
        }

        private static int Order(Type type)
        {
            if (type.IsAssignableFrom(typeof(ThingDef))) return 0;
            if (type.IsAssignableFrom(typeof(BackstoryDef))) return 1;
            if (type.IsAssignableFrom(typeof(TraitDef))) return 2;
            if (type.IsAssignableFrom(typeof(GeneDef))) return 3;
            return 100;
        }

        private static readonly HashSet<Type> blackListTypes = new HashSet<Type>
        {
            typeof(SoundDef), typeof(EffecterDef),
#if RW14
#else
            typeof(PawnRenderTreeDef), typeof(PawnRenderNodeTagDef)
#endif
        };

        private static readonly HashSet<string> blackListFields = new HashSet<string>
        {
            "alienRace"
        };

        private static void AddBlackList()
        {
            #region FacialAnimations

            blackListTypes.AddRange(typeof(Def).AllSubclassesNonAbstract().Where(x => x.Namespace == "FacialAnimation"));

            #endregion
        }

        public class DefInjectionUntranslatedParams
        {
            public Type defType;
            public Def def;
            public object parentObject;
            public FieldInfo field;
            public string normalizedPath;
            public string suggestedPath;

            public string original;
            public string translated;

            public List<string> originalCollection;
            public ConcurrentDictionary<string, string> translatedCollection;

            public bool isCollection;

            private bool _injected;

            public DefInjectionUntranslatedParams(string normalizedPath, string suggestedPath, string original, object parentObject, FieldInfo field, Def def)
            {
                this.normalizedPath = normalizedPath;
                this.suggestedPath = suggestedPath;
                this.original = original;
                this.parentObject = parentObject;
                this.field = field;
                this.def = def;
                this.defType = def.GetType();
                isCollection = false;
            }

            public DefInjectionUntranslatedParams(string normalizedPath, string suggestedPath,
                IEnumerable<string> originalCollection, object parentObject, FieldInfo field, Def def)
            {
                this.normalizedPath = normalizedPath;
                this.suggestedPath = suggestedPath;
                this.originalCollection = new List<string>(originalCollection);
                this.translatedCollection = new ConcurrentDictionary<string, string>();
                this.parentObject = parentObject;
                this.field = field;
                this.def = def;
                this.defType = def.GetType();
                isCollection = true;
            }


            public void InjectTranslation()
            {
                if (_injected)
                {
                    Log.Message("Already injected...");
                    return;
                }

                if (!isCollection)
                {
                    _injected = !_injected;
                    field.SetValue(parentObject, translated);
                    return;
                }
                lock (translatedCollection)
                {
                    if (originalCollection.Count == translatedCollection.Count)
                    {
                        _injected = !_injected;
                        var realList = (List<string>)field.GetValue(parentObject);
                        for (int i = 0; i < realList.Count; i++)
                        {
                            var prev = realList[i];
                            if (translatedCollection.TryGetValue(prev, out var t))
                            {
                                realList[i] = t;
                            }
                        }
                    }
                }
                
            }

            public void UndoInject()
            {
                if (!_injected)
                {
                    return;
                }


                if (!isCollection)
                {
                    field.SetValue(parentObject, original);
                    _injected = !_injected;
                    return;
                }

                lock (translatedCollection)
                {
                    if (originalCollection.Count > 0)
                    {
                        var realList = (List<string>)field.GetValue(parentObject);
                        if (realList.Count != originalCollection.Count)
                        {
                            Log.Warning(AutoTranslation.LogPrefix +
                                        $"Wrong collection size {realList.Count}vs{originalCollection.Count}, {def.defName}:{field.Name}");
                            return;
                        }
                        for (int i = 0; i < realList.Count; i++)
                        {
                            realList[i] = originalCollection[i];
                        }
                        _injected = !_injected;
                    }
                }
            }
        }
    }
}
