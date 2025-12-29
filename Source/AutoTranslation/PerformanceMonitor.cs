using System;
using System.Diagnostics;
using System.Threading;
using Verse;

namespace AutoTranslation
{
    /// <summary>
    /// Simple performance monitoring utility for tracking optimization improvements
    /// </summary>
    public static class PerformanceMonitor
    {
        private static Stopwatch _defInjectionTimer;
        private static int _totalDefsProcessed;
        private static int _totalFieldsAccessed;
        private static int _cacheHits;
        private static int _cacheMisses;

        public static void StartDefInjectionMonitoring()
        {
            _defInjectionTimer = Stopwatch.StartNew();
            _totalDefsProcessed = 0;
            _totalFieldsAccessed = 0;
            _cacheHits = 0;
            _cacheMisses = 0;
        }

        public static void StopDefInjectionMonitoring()
        {
            if (_defInjectionTimer == null) return;
            
            _defInjectionTimer.Stop();
            
            var elapsed = _defInjectionTimer.Elapsed;
            Log.Message(AutoTranslation.LogPrefix + 
                $"DefInjection Performance Report:\n" +
                $"  Total Time: {elapsed.TotalSeconds:F2}s\n" +
                $"  Defs Processed: {_totalDefsProcessed}\n" +
                $"  Fields Accessed: {_totalFieldsAccessed}\n" +
                $"  Avg Time per Def: {(elapsed.TotalMilliseconds / Math.Max(_totalDefsProcessed, 1)):F2}ms\n" +
                $"  ReflectionCache Size: {ReflectionCache.CacheSize}\n" +
                $"  ShouldTranslate Cache Hits: {_cacheHits}\n" +
                $"  ShouldTranslate Cache Misses: {_cacheMisses}\n" +
                $"  Cache Hit Rate: {(_cacheHits * 100.0 / Math.Max(_cacheHits + _cacheMisses, 1)):F1}%");
        }

        public static void RecordDefProcessed()
        {
            Interlocked.Increment(ref _totalDefsProcessed);
        }

        public static void RecordFieldAccessed()
        {
            Interlocked.Increment(ref _totalFieldsAccessed);
        }

        public static void RecordCacheHit()
        {
            Interlocked.Increment(ref _cacheHits);
        }

        public static void RecordCacheMiss()
        {
            Interlocked.Increment(ref _cacheMisses);
        }
    }
}

