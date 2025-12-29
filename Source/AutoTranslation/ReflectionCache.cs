using System;
using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace AutoTranslation
{
    /// <summary>
    /// Provides fast, compiled field access using Expression Trees.
    /// Replaces slow Reflection.GetValue() calls with compiled delegates (10-20x faster).
    /// </summary>
    public static class ReflectionCache
    {
        private static readonly ConcurrentDictionary<FieldInfo, Func<object, object>> _getterCache = 
            new ConcurrentDictionary<FieldInfo, Func<object, object>>();

        /// <summary>
        /// Gets a compiled getter delegate for the specified field.
        /// First call compiles the expression, subsequent calls return cached delegate.
        /// </summary>
        /// <param name="field">The field to create a getter for</param>
        /// <returns>A compiled delegate that retrieves the field value</returns>
        public static Func<object, object> GetCompiledGetter(FieldInfo field)
        {
            return _getterCache.GetOrAdd(field, CompileGetter);
        }

        /// <summary>
        /// Gets the field value using a compiled getter (much faster than Reflection).
        /// </summary>
        public static object GetValue(FieldInfo field, object instance)
        {
            if (instance == null) return null;
            
            try
            {
                var getter = GetCompiledGetter(field);
                return getter(instance);
            }
            catch (Exception)
            {
                // Fallback to reflection if compiled version fails
                return field.GetValue(instance);
            }
        }

        private static Func<object, object> CompileGetter(FieldInfo field)
        {
            try
            {
                // Create parameter: object obj
                var param = Expression.Parameter(typeof(object), "obj");
                
                // Cast to declaring type: (DeclaringType)obj
                var castParam = Expression.Convert(param, field.DeclaringType);
                
                // Access field: ((DeclaringType)obj).field
                var fieldAccess = Expression.Field(castParam, field);
                
                // Cast result to object: (object)((DeclaringType)obj).field
                var castResult = Expression.Convert(fieldAccess, typeof(object));
                
                // Compile to delegate
                return Expression.Lambda<Func<object, object>>(castResult, param).Compile();
            }
            catch (Exception)
            {
                // Fallback: return a delegate that uses reflection
                return obj => field.GetValue(obj);
            }
        }

        /// <summary>
        /// Clears the compiled getter cache.
        /// Useful for testing or memory management.
        /// </summary>
        public static void ClearCache()
        {
            _getterCache.Clear();
        }

        /// <summary>
        /// Gets the current cache size (number of compiled getters).
        /// </summary>
        public static int CacheSize => _getterCache.Count;
    }
}

