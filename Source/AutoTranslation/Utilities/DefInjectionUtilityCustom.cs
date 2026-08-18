using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AutoTranslation.Services;
using RimWorld;
using Verse;
using static Verse.DefInjectionPackage;

namespace AutoTranslation.Utilities
{
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
        private static readonly ConcurrentDictionary<(string value, string fieldName), bool> ShouldTranslateCache = 
            new ConcurrentDictionary<(string value, string fieldName), bool>();

        public static bool ShouldTranslate(string value, FieldInfo fi)
        {
            if (string.IsNullOrEmpty(value)) return false;

            var key = (value, fi?.Name ?? "");
            if (ShouldTranslateCache.TryGetValue(key, out var result))
            {
                return result;
            }

            // Single-hash miss path: the old TryGetValue + GetOrAdd(closure) combo hashed
            // the (potentially very long) string twice and allocated a closure per miss
            result = ShouldTranslateInternal(value, key.Item2);
            ShouldTranslateCache.TryAdd(key, result);
            return result;
        }

        private static bool ShouldTranslateInternal(string value, string fieldName)
        {
            if (BlacklistedFields.Contains(fieldName)) return false;
            if (value.Length < 2) return false;

            // Single cheap pass over the string; ordered so that data-grid rows
            // ("1,1,1,...", KCSG layouts etc.) are rejected without touching the
            // regexes or the language detector
            bool hasSlash = false, hasSpace = false, hasLetter = false, hasUnderscore = false, allDigits = true;
            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '/':
                    case '\\':
                        hasSlash = true;
                        break;
                    case ' ':
                        hasSpace = true;
                        break;
                    case '_':
                        hasUnderscore = true;
                        break;
                }
                if (!hasLetter && char.IsLetter(c)) hasLetter = true;
                if (allDigits && !char.IsDigit(c)) allDigits = false;
            }

            if (allDigits) return false;
            // No letters at all (numbers/symbols/separators only) -> never translatable.
            // char.IsLetter covers CJK/Cyrillic/etc., so real text always survives this.
            if (!hasLetter) return false;
            // Contains slashes but no spaces -> likely a path
            if (hasSlash && !hasSpace) return false;

            if (FilePathRegex.IsMatch(value)) return false;

            // Language detection (prevent re-translating already translated text).
            // By far the most expensive check - runs LAST, only on plausible candidates.
            if (Settings.EnableLanguageDetection && LanguageDetector.IsAlreadyInTargetLanguage(value))
                return false;

            // Field name based checks
            if (fieldName == "label" || fieldName == "description" ||
                fieldName.EndsWith("Label") || fieldName.EndsWith("Description"))
                return true;

            // `ABC_123` style identifiers with underscores but no spaces are likely IDs, not translatable text
            if (hasUnderscore && !hasSpace)
            {
                return false;
            }

            return true;
        }

        public static void FindMissingDefInjection(Action<DefInjectionUntranslatedParams> callBack)
        {
            AddBlackList();

            Interlocked.Exchange(ref _prunedObjects, 0);
            var swTotal = System.Diagnostics.Stopwatch.StartNew();

            var injectionsByNormalizedPath = new Dictionary<string, DefInjection>();
            foreach (var (key, value) in LanguageDatabase.activeLanguage.defInjections.SelectMany(x => x.injections))
            {
                if (!injectionsByNormalizedPath.ContainsKey(value.normalizedPath))
                    injectionsByNormalizedPath.Add(value.normalizedPath, value);
            }
            var msDictBuild = swTotal.ElapsedMilliseconds;

            // Use concurrent collection for parallel processing
            var resultBag = new ConcurrentBag<DefInjectionUntranslatedParams>();

            var packages = LanguageDatabase.activeLanguage.defInjections
                .Where(x => !blackListTypes.Any(black => x.defType.IsAssignableFrom(black)))
                .OrderBy(x => Order(x.defType))
                .ToList();

            // Flatten to (defType, def) pairs: parallelizing per def TYPE leaves most cores
            // idle because ThingDef alone holds the majority of defs in big modpacks -
            // per-def work items keep every core busy
            var workItems = new List<(Type defType, Def def)>(1 << 14);
            foreach (var package in packages)
            {
                if (!package.defType.IsSubclassOf(typeof(Def)))
                {
                    Log.Message(AutoTranslation.LogPrefix + $"Type {package.defType.Name} isn't subclass of Def");
                    continue;
                }
                foreach (var def in GenDefDatabase.GetAllDefsInDatabaseForDef(package.defType))
                {
                    workItems.Add((package.defType, def));
                }
            }
            var msFlatten = swTotal.ElapsedMilliseconds - msDictBuild;

            // Per-defType timing for instrumentation (ticks)
            var ticksByType = new ConcurrentDictionary<Type, long>();
            long stringsVisited = 0;
            long defsProcessed = 0;

            // GC instrumentation: if collections spike during traverse, the scan is
            // allocation-bound (Boehm GC is stop-the-world), not CPU-bound
            var gcGen = Math.Max(GC.MaxGeneration, 0);
            var gcCountBefore = 0;
            for (int g = 0; g <= gcGen; g++) gcCountBefore += GC.CollectionCount(g);
            var memBefore = GC.GetTotalMemory(false);

            // Watchdog: if the scan ever stalls again, the log shows whether progress
            // is advancing (slow) or frozen (bug) and how far it got
            var watchdog = new System.Threading.Timer(_ =>
            {
                Log.Message(AutoTranslation.LogPrefix +
                    $"DefInjected scan in progress: {System.Threading.Interlocked.Read(ref defsProcessed)}/{workItems.Count} defs, " +
                    $"strings={System.Threading.Interlocked.Read(ref stringsVisited)}, pruned={System.Threading.Interlocked.Read(ref _prunedObjects)}");
            }, null, 15000, 15000);

            try
            {
            Parallel.ForEach(workItems,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
                item =>
                {
                    var swDef = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        TraverseDefFast(item.def,
                        (parentPath, leafName, isCollection, value, enumerableValue, parentObject, fi, def) =>
                        {
                            Interlocked.Increment(ref stringsVisited);
                            if (!isCollection)
                            {
                                // Skip if we shouldn't translate this value - no path string is
                                // ever built for the ~98% of strings that fail this check
                                if (!ShouldTranslate(value, fi)) return;

                                var normalizedPath = TraversalPath.BuildNormalized(parentPath, leafName);

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
                                    var suggestedPath = TraversalPath.BuildSuggested(parentPath, leafName, normalizedPath);
                                    resultBag.Add(new DefInjectionUntranslatedParams(normalizedPath, suggestedPath, value,
                                        parentObject, fi, def));
                                }
                            }
                            else
                            {
                                var normalizedPath = TraversalPath.BuildNormalized(parentPath, leafName);

                                if (injectionsByNormalizedPath.TryGetValue(normalizedPath, out var defInjection) && defInjection.IsFullListInjection)
                                {
                                    // full-list injection already present - nothing to do
                                }
                                else
                                {
                                    if (normalizedPath.Contains("rulesFiles")) return;

                                    int num = 0;
                                    bool listHasMissingItems = false;
                                    var lst = enumerableValue.ToList();

                                    foreach (var element in lst)
                                    {
                                        // Filter elements we shouldn't translate
                                        if (!ShouldTranslate(element, fi))
                                        {
                                            num++;
                                            continue;
                                        }

                                        var key = normalizedPath + "." + num;

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
                                        var suggestedPath = TraversalPath.BuildSuggested(parentPath, leafName, normalizedPath);
                                        resultBag.Add(new DefInjectionUntranslatedParams(normalizedPath, suggestedPath, lst, parentObject, fi, def));
                                    }
                                }
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        // Skip malformed defs; usually harmless with certain mod combinations
                        if (Prefs.DevMode)
                        {
                            Log.Warning($"{AutoTranslation.LogPrefix} Error processing Def '{item.def?.defName ?? "unknown"}' of type {item.defType.Name}: {ex.Message}");
                        }
                    }
                    finally
                    {
                        swDef.Stop();
                        System.Threading.Interlocked.Increment(ref defsProcessed);
                        ticksByType.AddOrUpdate(item.defType, swDef.ElapsedTicks, (_, old) => old + swDef.ElapsedTicks);
                    }
                });
            }
            finally
            {
                watchdog.Dispose();
            }

            var gcCountAfter = 0;
            for (int g = 0; g <= gcGen; g++) gcCountAfter += GC.CollectionCount(g);
            var memAfter = GC.GetTotalMemory(false);

            var msTraverse = swTotal.ElapsedMilliseconds - msDictBuild - msFlatten;

            // Process results
            foreach (var result in resultBag)
            {
                callBack(result);
            }
            swTotal.Stop();
            var msCallbacks = swTotal.ElapsedMilliseconds - msDictBuild - msFlatten - msTraverse;

            // Instrumentation summary: phase timings + slowest def types (CPU time summed
            // across threads, so type totals can exceed the wall-clock traverse time)
            var slowest = ticksByType.OrderByDescending(x => x.Value).Take(10)
                .Select(x => $"{x.Key.Name}={x.Value * 1000 / System.Diagnostics.Stopwatch.Frequency}ms");
            Log.Message(AutoTranslation.LogPrefix +
                $"FindMissingDefInjection: total={swTotal.ElapsedMilliseconds}ms " +
                $"(dict={msDictBuild}ms, flatten={msFlatten}ms, traverse={msTraverse}ms, callbacks={msCallbacks}ms), " +
                $"defs={workItems.Count}, strings={stringsVisited}, missing={resultBag.Count}, " +
                $"handleFails={_brokenHandleElementTypes.Count}, pruned={System.Threading.Interlocked.Read(ref _prunedObjects)}, " +
                $"gcCollections={gcCountAfter - gcCountBefore}, memDelta={(memAfter - memBefore) / (1024 * 1024)}MB " +
                $"| slowest types (CPU): {string.Join(", ", slowest)}");
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

        // Element types for which TranslationHandleUtility threw: it will throw for EVERY
        // element of that type, and exceptions cost tens of microseconds each - across
        // hundreds of thousands of list elements that alone is minutes of startup time
        private static readonly ConcurrentDictionary<Type, byte> _brokenHandleElementTypes = new ConcurrentDictionary<Type, byte>();

        // TranslationHandleUtility.NormalizedHandle mutates a SHARED static StringBuilder,
        // so concurrent calls from parallel workers silently garble handles or throw.
        // Vanilla only ever calls it from the main thread - we must serialize our calls.
        private static readonly object _handleLock = new object();

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
                        if (item != null && !(item is Def) && GenTypes.IsCustomType(item.GetType()) &&
                            !TryPrune(item.GetType()))
                        {
                            string handle;
                            var itemType = item.GetType();
                            if (_brokenHandleElementTypes.ContainsKey(itemType))
                            {
                                // Known to throw for this type - skip straight to the index fallback
                                handle = idx.ToString();
                            }
                            else
                            {
                                try
                                {
                                    lock (_handleLock)
                                    {
                                        handle = TranslationHandleUtility.GetBestHandleWithIndexForListElement(nxtCollection, item);
                                    }
                                    if (string.IsNullOrEmpty(handle))
                                        handle = idx.ToString();
                                }
                                catch (Exception)
                                {
                                    // RimWorld's TranslationHandleUtility can fail with certain data structures
                                    // This is normal for some vanilla/mod defs - just fall back to using the index
                                    _brokenHandleElementTypes.TryAdd(itemType, 0);
                                    handle = idx.ToString();
                                }
                            }
                            var nxtNormalizedPath = $"{curNormalizedPath}.{field.Name}.{idx}";
                            var nxtSuggestedPath = $"{curSuggestedPath}.{field.Name}.{handle}";
                            ForEachPossibleDefInjectionInDefRecursive(item, nxtNormalizedPath, nxtSuggestedPath, visited, def, action, depth + 1);
                        }
                        idx++;
                    }
                }
                else if (nxt != null && GenTypes.IsCustomType(nxt.GetType()) && !TryPrune(nxt.GetType()))
                {
                    ForEachPossibleDefInjectionInDefRecursive(nxt, curNormalizedPath + "." + field.Name,
                        curSuggestedPath + "." + field.Name, visited, def, action, depth + 1);
                }
            }
        }

        #region Plan-based fast traversal

        // Allocation-first rewrite of the recursive traverser. Two ideas:
        //  1. Per-TYPE precompiled plan: field filtering (blacklists, primitives, Defs,
        //     statically string-unreachable types) and kind classification happen ONCE per
        //     type instead of per object x per field.
        //  2. Lazy paths: instead of concatenating path strings at every recursion step
        //     (the dominant allocation source - ~1GB per modpack scan), a lightweight
        //     parent-chain node is passed down and the actual strings (plus the O(n^2)
        //     list-handle lookup and TKey suggestion) are built only for the few thousand
        //     strings that pass ShouldTranslate.
        // Semantics are identical to the old traverser; the old one is kept for reference.

        internal delegate void LazyTraverser(TraversalPath parentPath, string leafName, bool isCollection,
            string value, IEnumerable<string> valueEnumerable, object parentObject, FieldInfo fi, Def def);

        internal sealed class TraversalPath
        {
            private readonly TraversalPath _parent;
            private readonly string _segment;      // normalized segment (field name, or index for list elements)
            private readonly object _listRef;      // list-element nodes only (for lazy handle lookup)
            private readonly object _elementRef;
            private string _suggestedSegment;      // lazily computed for list-element nodes

            public TraversalPath(TraversalPath parent, string segment)
            {
                _parent = parent;
                _segment = segment;
                _suggestedSegment = segment;
            }

            public TraversalPath(TraversalPath parent, int index, object listRef, object elementRef)
            {
                _parent = parent;
                _segment = index.ToString();
                _listRef = listRef;
                _elementRef = elementRef;
            }

            private string SuggestedSegment
            {
                get
                {
                    if (_suggestedSegment != null) return _suggestedSegment;
                    return _suggestedSegment = ComputeHandleSegment();
                }
            }

            private string ComputeHandleSegment()
            {
                var itemType = _elementRef.GetType();
                if (!_brokenHandleElementTypes.ContainsKey(itemType))
                {
                    try
                    {
                        string handle;
                        lock (_handleLock)
                        {
                            handle = TranslationHandleUtility.GetBestHandleWithIndexForListElement(_listRef, _elementRef);
                        }
                        if (!string.IsNullOrEmpty(handle)) return handle;
                    }
                    catch (Exception)
                    {
                        _brokenHandleElementTypes.TryAdd(itemType, 0);
                    }
                }
                return _segment; // index fallback
            }

            public static string BuildNormalized(TraversalPath parent, string leafName)
            {
                var sb = new StringBuilder(64);
                parent.AppendTo(sb, suggested: false);
                sb.Append('.').Append(leafName);
                return sb.ToString();
            }

            /// <summary>
            /// Mirrors the old traverser: the TKey suggestion is tried on the full
            /// normalized leaf path; otherwise segments (with lazy list handles) are joined.
            /// </summary>
            public static string BuildSuggested(TraversalPath parent, string leafName, string fullNormalizedPath)
            {
                if (TKeySystem.TrySuggestTKeyPath(fullNormalizedPath, out var tKeyPath))
                    return tKeyPath;

                var sb = new StringBuilder(64);
                parent.AppendTo(sb, suggested: true);
                sb.Append('.').Append(leafName);
                return sb.ToString();
            }

            private void AppendTo(StringBuilder sb, bool suggested)
            {
                _parent?.AppendTo(sb, suggested);
                if (_parent != null) sb.Append('.');
                sb.Append(suggested ? SuggestedSegment : _segment);
            }
        }

        private enum PlanKind : byte
        {
            StringField,       // field statically typed as string
            StringEnumerable,  // field statically typed as IEnumerable<string>
            Enumerable,        // other enumerable - elements inspected at runtime
            ObjectFixed,       // sealed/value custom type, statically known string-reachable
            ObjectPolymorphic, // custom class field - runtime type decides reachability
            RuntimeDispatch    // object/interface field - full runtime dispatch
        }

        private sealed class PlanEntry
        {
            public Func<object, object> Getter;
            public FieldInfo Field;
            public string Name;
            public PlanKind Kind;
        }

        private static readonly ConcurrentDictionary<Type, PlanEntry[]> _planCache = new ConcurrentDictionary<Type, PlanEntry[]>();

        private static PlanEntry[] GetPlan(Type type)
        {
            return _planCache.GetOrAdd(type, BuildPlan);
        }

        private static PlanEntry[] BuildPlan(Type t)
        {
            var list = new List<PlanEntry>();
            foreach (var field in GetFieldsOptimized(t))
            {
                if (blackListFields.Contains(field.Name) || BlacklistedFields.Contains(field.Name)) continue;

                var ft = field.FieldType;
                PlanKind kind;

                if (typeof(string).IsAssignableFrom(ft))
                {
                    kind = PlanKind.StringField;
                }
                else if (typeof(IEnumerable<string>).IsAssignableFrom(ft))
                {
                    kind = PlanKind.StringEnumerable;
                }
                else if (typeof(Def).IsAssignableFrom(ft))
                {
                    continue; // traverser never enters Defs
                }
                else if (ft.IsPrimitive || ft.IsEnum || _skipTypes.Contains(ft))
                {
                    continue;
                }
                else if (ft == typeof(object) || ft.IsInterface)
                {
                    kind = PlanKind.RuntimeDispatch;
                }
                else if (typeof(IEnumerable).IsAssignableFrom(ft))
                {
                    // Statically dead element types never make it into the plan
                    var elem = GetEnumerableElementType(ft);
                    if (elem != null && !typeof(string).IsAssignableFrom(elem) && elem != typeof(object) && !elem.IsInterface)
                    {
                        if (typeof(Def).IsAssignableFrom(elem)) continue;
                        if (!GenTypes.IsCustomType(elem)) continue; // traverser only recurses into custom elements
                        if (!TypeOrSubtypesCanReachString(elem, new HashSet<Type>())) continue;
                    }
                    kind = PlanKind.Enumerable;
                }
                else if (GenTypes.IsCustomType(ft))
                {
                    if (!FieldTypeCanReachString(ft, new HashSet<Type>())) continue; // statically dead (subclass-aware)
                    kind = ft.IsSealed || ft.IsValueType ? PlanKind.ObjectFixed : PlanKind.ObjectPolymorphic;
                }
                else
                {
                    continue; // non-custom class (System.Type etc.) - never recursed into
                }

                list.Add(new PlanEntry
                {
                    Getter = ReflectionCache.GetCompiledGetter(field),
                    Field = field,
                    Name = field.Name,
                    Kind = kind
                });
            }
            return list.ToArray();
        }

        internal static void TraverseDefFast(Def def, LazyTraverser action)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            TraverseFast(def, new TraversalPath(null, def.defName), visited, def, action, 0);
        }

        private static void TraverseFast(object cur, TraversalPath path, HashSet<object> visited, Def def, LazyTraverser action, int depth)
        {
            if (cur == null || cur is Thing) return;
            if (depth >= MaxRecursionDepth) return;

            var curType = cur.GetType();
            if (_skipTypes.Contains(curType)) return;

            if (!curType.IsValueType)
            {
                if (!visited.Add(cur)) return;
            }

            var plan = GetPlan(curType);
            for (int i = 0; i < plan.Length; i++)
            {
                var entry = plan[i];
                var nxt = entry.Getter(cur);
                if (nxt == null) continue;

                switch (entry.Kind)
                {
                    case PlanKind.StringField:
                        action(path, entry.Name, false, (string)nxt, null, cur, entry.Field, def);
                        break;

                    case PlanKind.StringEnumerable:
                        action(path, entry.Name, true, null, (IEnumerable<string>)nxt, cur, entry.Field, def);
                        break;

                    case PlanKind.ObjectFixed:
                        TraverseFast(nxt, new TraversalPath(path, entry.Name), visited, def, action, depth + 1);
                        break;

                    case PlanKind.ObjectPolymorphic:
                    {
                        if (nxt is Def) break;
                        var rt = nxt.GetType();
                        if (!CanContainTranslatableString(rt))
                        {
                            Interlocked.Increment(ref _prunedObjects);
                            break;
                        }
                        TraverseFast(nxt, new TraversalPath(path, entry.Name), visited, def, action, depth + 1);
                        break;
                    }

                    case PlanKind.Enumerable:
                        if (nxt is IEnumerable<string> stringSeq)
                        {
                            action(path, entry.Name, true, null, stringSeq, cur, entry.Field, def);
                        }
                        else if (nxt is IEnumerable seq)
                        {
                            TraverseElements(seq, path, entry.Name, visited, def, action, depth);
                        }
                        break;

                    case PlanKind.RuntimeDispatch:
                    {
                        // Mirrors the old traverser's runtime chain for object/interface fields
                        if (nxt is Def || nxt is string) break;
                        if (nxt is IEnumerable<string> ss)
                        {
                            action(path, entry.Name, true, null, ss, cur, entry.Field, def);
                        }
                        else if (nxt is IEnumerable en)
                        {
                            TraverseElements(en, path, entry.Name, visited, def, action, depth);
                        }
                        else
                        {
                            var rt = nxt.GetType();
                            if (!GenTypes.IsCustomType(rt)) break;
                            if (!CanContainTranslatableString(rt))
                            {
                                Interlocked.Increment(ref _prunedObjects);
                                break;
                            }
                            TraverseFast(nxt, new TraversalPath(path, entry.Name), visited, def, action, depth + 1);
                        }
                        break;
                    }
                }
            }
        }

        private static void TraverseElements(IEnumerable collection, TraversalPath parentPath, string fieldName,
            HashSet<object> visited, Def def, LazyTraverser action, int depth)
        {
            TraversalPath fieldNode = null; // created only if an element is actually recursed into
            int idx = 0;
            foreach (var item in collection)
            {
                if (item == null || item is Def)
                {
                    idx++;
                    continue;
                }
                var itemType = item.GetType();
                if (!GenTypes.IsCustomType(itemType))
                {
                    idx++;
                    continue;
                }
                if (!CanContainTranslatableString(itemType))
                {
                    Interlocked.Increment(ref _prunedObjects);
                    idx++;
                    continue;
                }

                if (fieldNode == null) fieldNode = new TraversalPath(parentPath, fieldName);
                // The list-element handle (the expensive O(n) vanilla lookup) is resolved
                // lazily inside TraversalPath, and only when a suggested path is materialized
                TraverseFast(item, new TraversalPath(fieldNode, idx, collection, item), visited, def, action, depth + 1);
                idx++;
            }
        }

        #endregion

        #region String reachability pruning

        // Type-level memoized answer to "can traversing an object of this runtime type ever
        // reach a translatable string?". Subtrees that can't (ThinkNode graphs, layout grids,
        // mod data defs) are skipped entirely - including the O(n^2) list handle computation.
        // Conservative: any uncertainty (object/interface fields, cycles) counts as reachable,
        // so pruning can never hide a real translation target.
        private static readonly ConcurrentDictionary<Type, bool> _stringReachCache = new ConcurrentDictionary<Type, bool>();
        private static readonly ConcurrentDictionary<Type, bool> _typeWithSubsReachCache = new ConcurrentDictionary<Type, bool>();
        private static long _prunedObjects;

        // GenTypes.AllSubclasses caches into a plain Dictionary without locking - calling it
        // from parallel workers corrupts it (infinite loops / duplicate-key exceptions).
        // Keep our own thread-safe cache built from the read-only AllTypes list instead.
        private static readonly ConcurrentDictionary<Type, List<Type>> _subclassCache = new ConcurrentDictionary<Type, List<Type>>();

        private static List<Type> GetSubclassesThreadSafe(Type baseType)
        {
            return _subclassCache.GetOrAdd(baseType,
                bt => GenTypes.AllTypes.Where(x => x.IsSubclassOf(bt)).ToList());
        }

        private static bool CanContainTranslatableString(Type type)
        {
            if (_stringReachCache.TryGetValue(type, out var cached)) return cached;
            return ReachDfs(type, new HashSet<Type>());
        }

        /// <summary>True (and counted) when the subtree rooted at this runtime type cannot contain strings.</summary>
        private static bool TryPrune(Type runtimeType)
        {
            if (CanContainTranslatableString(runtimeType)) return false;
            System.Threading.Interlocked.Increment(ref _prunedObjects);
            return true;
        }

        private static bool ReachDfs(Type t, HashSet<Type> visiting)
        {
            if (_stringReachCache.TryGetValue(t, out var cached)) return cached;
            if (!visiting.Add(t))
            {
                // Type cycle: assume reachable (conservative) so every query terminates and
                // memoizes. The previous "tentative, don't memoize" treatment caused
                // exponential re-exploration of big cyclic hierarchies (ThinkNode/QuestNode)
                // - an effectively infinite scan on vanilla+DLC.
                return true;
            }

            try
            {
                foreach (var field in GetFieldsOptimized(t))
                {
                    if (blackListFields.Contains(field.Name) || BlacklistedFields.Contains(field.Name)) continue;
                    if (FieldTypeCanReachString(field.FieldType, visiting))
                    {
                        _stringReachCache[t] = true;
                        return true;
                    }
                }
                // With cycles counted as reachable, a false result never depends on an
                // unresolved back-edge, so it is final and safe to memoize
                _stringReachCache[t] = false;
                return false;
            }
            finally
            {
                visiting.Remove(t);
            }
        }

        // Mirrors exactly what the traverser can reach: string fields, IEnumerable<string>
        // fields, recursion into custom-type fields and custom-type list elements.
        private static bool FieldTypeCanReachString(Type ft, HashSet<Type> visiting)
        {
            if (typeof(string).IsAssignableFrom(ft)) return true;
            if (typeof(IEnumerable<string>).IsAssignableFrom(ft)) return true;
            if (typeof(Def).IsAssignableFrom(ft)) return false; // traverser never enters Defs
            if (ft.IsPrimitive || ft.IsEnum || _skipTypes.Contains(ft)) return false;
            if (ft == typeof(object) || ft.IsInterface) return true; // conservative

            if (typeof(IEnumerable).IsAssignableFrom(ft))
            {
                var elem = GetEnumerableElementType(ft);
                if (elem == null) return true; // unknown element type - conservative
                if (typeof(string).IsAssignableFrom(elem)) return true;
                if (typeof(Def).IsAssignableFrom(elem)) return false;
                if (elem == typeof(object) || elem.IsInterface) return true;
                if (!GenTypes.IsCustomType(elem)) return false; // traverser only recurses into custom elements
                return TypeOrSubtypesCanReachString(elem, visiting);
            }

            if (!GenTypes.IsCustomType(ft)) return false; // traverser only recurses into custom types
            return TypeOrSubtypesCanReachString(ft, visiting);
        }

        // A field declared as a base type can hold any loaded subclass at runtime,
        // so the union over the base type and all subclasses decides reachability.
        private static bool TypeOrSubtypesCanReachString(Type baseType, HashSet<Type> visiting)
        {
            if (_typeWithSubsReachCache.TryGetValue(baseType, out var cached))
            {
                return cached;
            }

            var result = ReachDfs(baseType, visiting);
            if (!result && baseType.IsClass && !baseType.IsSealed)
            {
                foreach (var sub in GetSubclassesThreadSafe(baseType))
                {
                    if (ReachDfs(sub, visiting))
                    {
                        result = true;
                        break;
                    }
                }
            }

            _typeWithSubsReachCache[baseType] = result;
            return result;
        }

        private static Type GetEnumerableElementType(Type ft)
        {
            if (ft.IsArray) return ft.GetElementType();
            if (ft.IsGenericType && ft.GetGenericArguments().Length == 1) return ft.GetGenericArguments()[0];
            foreach (var iface in ft.GetInterfaces())
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                    return iface.GetGenericArguments()[0];
            }
            return null;
        }

        #endregion

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
            // Huge recursive node graphs with zero player-visible text - measured at
            // hundreds of CPU-seconds in large modpacks for nothing
            typeof(ThinkTreeDef), typeof(Verse.AI.DutyDef),
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
    
    internal class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        
        public new bool Equals(object x, object y)
        {
            return ReferenceEquals(x, y);
        }
        
        public int GetHashCode(object obj)
        {
            return RuntimeHelpers.GetHashCode(obj);
        }
    }
    
}

