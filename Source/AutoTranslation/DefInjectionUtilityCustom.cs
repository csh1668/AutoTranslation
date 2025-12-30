using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RimWorld;
using Verse;
using static Verse.DefInjectionPackage;

namespace AutoTranslation
{
    // Custom comparer that uses reference equality only (doesn't call GetHashCode/Equals)
    internal class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        
        public new bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }
        
        public int GetHashCode(object obj)
        {
            return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }
    }

    public static class DefInjectionUtilityCustom
    {
        public delegate void Traverser(string normalizedPath, string suggestedPath, bool isCollection, string curValue,
            IEnumerable<string> curValueEnumerable, object parentObject, FieldInfo fi, Def def);
        
        private static readonly ConcurrentDictionary<Type, List<FieldInfo>> fieldsCached = new ConcurrentDictionary<Type, List<FieldInfo>>();
        
        // Fields that should never be translated
        private static readonly HashSet<string> BlacklistedFields = new HashSet<string>
        {
            "alienRace", "texPath", "graphicPath", "soundDef", "effecter", 
            "iconPath", "shader", "soundCast", "soundCastTail", "soundInteract",
            "soundHitPawn", "soundMiss", "soundMeleeHit", "soundMeleeMiss",
            "soundAmbience", "linkSound"
        };

        // Regular expressions for detecting file paths or non-translatable content
        private static readonly Regex FilePathRegex = new Regex(@"^[\w\/\.\-\\]+\.(png|jpg|jpeg|wav|mp3|ogg|xml|txt|lua|tex|dds)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PathLikeRegex = new Regex(@"[\/\\]", RegexOptions.Compiled);
        private static readonly Regex OnlySymbolsRegex = new Regex(@"^[^\w\s]+$", RegexOptions.Compiled);
        private static readonly Regex IdLikeRegex = new Regex(@"^[a-zA-Z0-9_]+$", RegexOptions.Compiled);

        // Cache for ShouldTranslate results to avoid redundant checks
        private static readonly ConcurrentDictionary<(string value, string fieldName), bool> _shouldTranslateCache = 
            new ConcurrentDictionary<(string value, string fieldName), bool>();

        public static bool ShouldTranslate(string value, FieldInfo fi)
        {
            // Fast path for null/empty
            if (string.IsNullOrEmpty(value) || string.IsNullOrWhiteSpace(value)) return false;
            
            // Check cache first
            var key = (value, fi?.Name ?? "");
            if (_shouldTranslateCache.TryGetValue(key, out var result))
            {
                return result;
            }
            
            return _shouldTranslateCache.GetOrAdd(key, k => ShouldTranslateInternal(k.value, k.fieldName, fi));
        }

        private static bool ShouldTranslateInternal(string value, string fieldName, FieldInfo fi)
        {
            
            // 0. Language detection check (prevent re-translating already translated text)
            // 이미 목표 언어로 작성된 텍스트는 번역하지 않음
            if (Settings.EnableLanguageDetection && LanguageDetector.IsAlreadyInTargetLanguage(value))
            {
                return false;
            }
            
            if (BlacklistedFields.Contains(fieldName)) return false;

            // 1. Length check (cheap, do early)
            if (value.Length < 2) return false;

            // 2. Numeric-only check (cheap)
            // 숫자만 있는 경우 번역 불필요
            if (value.All(char.IsDigit)) return false;

            // 3. Character frequency checks (faster than regex)
            int slashCount = 0;
            bool hasSpace = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '/' || c == '\\') slashCount++;
                if (c == ' ') hasSpace = true;
                if (slashCount > 0 && hasSpace) break; // Early exit
            }
            
            // Contains slashes but no spaces -> likely a path
            if (slashCount > 0 && !hasSpace) return false;

            // 4. Regex checks (expensive, do last)
            if (FilePathRegex.IsMatch(value)) return false;
            if (OnlySymbolsRegex.IsMatch(value)) return false;
            
            // 5. Field name based checks (after basic validation)
            // label/description 필드는 번역 대상
            if (fieldName == "label" || fieldName == "description" || 
                fieldName.EndsWith("Label") || fieldName.EndsWith("Description")) 
                return true;
            
            // 6. ID heuristic: 언더스코어가 있으면 ID로 간주
            // 예: "Building_Roof_Metal", "Apparel_Pants_Worker"
            if (value.Contains('_') && !hasSpace)
            {
                return false;
            }
            
            return true;
        }

        public static void FindMissingDefInjection(Action<DefInjectionUntranslatedParams> callBack)
        {
            AddBlackList();

            var injectionsByNormalizedPath = new Dictionary<string, DefInjection>();
            foreach (var (key, value) in LanguageDatabase.activeLanguage.defInjections.SelectMany(x => x.injections))
            {
                if (!injectionsByNormalizedPath.ContainsKey(value.normalizedPath))
                    injectionsByNormalizedPath.Add(value.normalizedPath, value);
            }
            
            // Use concurrent collection for parallel processing
            var resultBag = new ConcurrentBag<DefInjectionUntranslatedParams>();
            
            // Parallel processing by DefInjection package
            var packages = LanguageDatabase.activeLanguage.defInjections
                .Where(x => !blackListTypes.Any(black => x.defType.IsAssignableFrom(black)))
                .OrderBy(x => Order(x.defType))
                .ToList();

            Parallel.ForEach(packages,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                defInjectionPackage =>
                {
                    ForEachPossibleDefInjection(defInjectionPackage.defType,
                        (normalizedPath, suggestedPath, isCollection, value, enumerableValue, parentObject, fi, def) =>
                        {
                            if (!isCollection)
                            {
                                // Skip if we shouldn't translate this value
                                if (!ShouldTranslate(value, fi)) return;

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
                                resultBag.Add(new DefInjectionUntranslatedParams(normalizedPath, suggestedPath, value,
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
                            if (normalizedPath.Contains("rulesFiles")) return;
                            
                            int num = 0;
                            bool listHasMissingItems = false;
                            var lst = enumerableValue.ToList();
                            
                            foreach (var element in lst)
                            {
                                var key = normalizedPath + "." + num;
                                var curSuggestedPath = suggestedPath + "." + num;

                                // Filter elements we shouldn't translate
                                if (!ShouldTranslate(element, fi))
                                {
                                    num++;
                                    continue;
                                }

                                bool flag = false;
                                if (injectionsByNormalizedPath.TryGetValue(key, out var defInjection2) && !defInjection2.IsFullListInjection)
                                {
                                    if (defInjection2.isPlaceholder)
                                        flag = true;
                                }
                                else flag = true;

                                if (flag && DefInjectionUtility.ShouldCheckMissingInjection(element, fi, def))
                                {
                                    listHasMissingItems = true;
                                }

                                num++;
                            }
                            
                            // Add the list once if it has any missing items
                            if (listHasMissingItems)
                            {
                                resultBag.Add(new DefInjectionUntranslatedParams(normalizedPath, suggestedPath, lst, parentObject, fi, def));
                            }
                        }
                    });
                });
            
            // Process results
            foreach (var result in resultBag)
            {
                callBack(result);
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
                try
                {
                    ForEachPossibleDefInjectionInDef(def, action);
                }
                catch (Exception ex)
                {
                    // Skip malformed defs that cause processing errors
                    // Only log in DevMode as these are usually harmless and expected with certain mod combinations
                    if (Prefs.DevMode)
                    {
                        Log.Warning($"{AutoTranslation.LogPrefix} Error processing Def '{def?.defName ?? "unknown"}' of type {defType.Name}: {ex.Message}");
                    }
                }
            }
        }

        private const int MaxRecursionDepth = 15;

        private static void ForEachPossibleDefInjectionInDef(Def def, Traverser action)
        {
            // Use ReferenceEqualityComparer to avoid calling GetHashCode/Equals on potentially problematic objects
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            ForEachPossibleDefInjectionInDefRecursive(def, def.defName, def.defName, visited, def, action, 0);
        }

        private static void ForEachPossibleDefInjectionInDefRecursive(object cur, string curNormalizedPath, string curSuggestedPath, HashSet<object> visited, Def def, Traverser action, int depth)
        {
            if (cur == null || cur is Thing) return;
            
            // Prevent stack overflow by limiting recursion depth
            if (depth >= MaxRecursionDepth) return;
            
            // Skip primitive and common value types (performance optimization)
            var curType = cur.GetType();
            if (_skipTypes.Contains(curType)) return;
            
            if (!curType.IsValueType)
            {
                if (visited.Contains(cur)) return;
                visited.Add(cur);
            }
            
            foreach (var field in GetFieldsOptimized(cur.GetType()))
            {
                if (blackListFields.Contains(field.Name) || BlacklistedFields.Contains(field.Name)) continue;

                var nxt = ReflectionCache.GetValue(field, cur);
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
                            string handle;
                            try
                            {
                                handle = TranslationHandleUtility.GetBestHandleWithIndexForListElement(nxtCollection, item);
                                if (string.IsNullOrEmpty(handle))
                                    handle = idx.ToString();
                            }
                            catch (Exception)
                            {
                                // RimWorld's TranslationHandleUtility can fail with certain data structures
                                // This is normal for some vanilla/mod defs - just fall back to using the index
                                // No logging needed as this is expected behavior
                                handle = idx.ToString();
                            }
                            var nxtNormalizedPath = $"{curNormalizedPath}.{field.Name}.{idx}";
                            var nxtSuggestedPath = $"{curSuggestedPath}.{field.Name}.{handle}";
                            ForEachPossibleDefInjectionInDefRecursive(item, nxtNormalizedPath, nxtSuggestedPath, visited, def, action, depth + 1);
                        }
                        idx++;
                    }
                }
                else if (nxt != null && GenTypes.IsCustomType(nxt.GetType()))
                {
                    ForEachPossibleDefInjectionInDefRecursive(nxt, curNormalizedPath + "." + field.Name,
                        curSuggestedPath + "." + field.Name, visited, def, action, depth + 1);
                }
            }
        }

        private static List<FieldInfo> GetFieldsOptimized(Type type)
        {
            return fieldsCached.GetOrAdd(type, t =>
            {
                return t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => !field.HasAttribute<UnsavedAttribute>() && !field.HasAttribute<NoTranslateAttribute>())
                    .OrderByDescending(field => field.Name == "label")
                    .ThenByDescending(field => field.Name == "description").ToList();
            });
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

        // Types that should be skipped during recursive traversal (performance optimization)
        private static readonly HashSet<Type> _skipTypes = new HashSet<Type>
        {
            typeof(int), typeof(float), typeof(double), typeof(bool), typeof(byte), 
            typeof(short), typeof(long), typeof(uint), typeof(ushort), typeof(ulong),
            typeof(decimal), typeof(char), typeof(sbyte),
            typeof(UnityEngine.Vector2), typeof(UnityEngine.Vector3), typeof(UnityEngine.Vector4),
            typeof(UnityEngine.Color), typeof(UnityEngine.Color32),
            typeof(Verse.IntVec2), typeof(Verse.IntVec3), typeof(Verse.Rot4),
            typeof(Verse.CellRect), typeof(UnityEngine.Rect)
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
                    // Log.Message("Already injected...");
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
                if (!_injected) return;

                if (!isCollection)
                {
                    field.SetValue(parentObject, original);
                    _injected = false;
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

                        _injected = false;
                    }
                }
            }

            public void ClearTranslation()
            {
                if (_injected) UndoInject();

                if (!isCollection)
                {
                    translated = null;
                }
                else
                {
                    lock (translatedCollection)
                    {
                        translatedCollection.Clear();
                    }
                }
                _injected = false;
            }
        }
    }
}
