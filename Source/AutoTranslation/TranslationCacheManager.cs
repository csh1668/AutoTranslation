using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Verse;

namespace AutoTranslation
{
    public class TranslationCacheManager
    {
        private static TranslationCacheManager _instance;
        public static TranslationCacheManager Instance => _instance ?? (_instance = new TranslationCacheManager());

        public ConcurrentDictionary<string, string> Cache { get; private set; } = new ConcurrentDictionary<string, string>();
        
        public static bool IsDirty { get; private set; }
        
        // ModId가 없는 레거시 엔트리에 부여할 기본 ModId
        public static readonly string LEGACY_MOD_ID = "unknown.mod";
        public string CacheDirectory
        {
            get
            {
                var path = Path.Combine(GenFilePaths.SaveDataFolderPath, "AutoTranslation");
                var directoryInfo = new DirectoryInfo(path);
                if (!directoryInfo.Exists)
                {
                    directoryInfo.Create();
                }
                return path;
            }
        }

        private TranslationCacheManager()
        {
        }

        public static void Load(string name)
        {
            Instance.LoadInternal(name);
        }

        public static void Save(string name)
        {
            Instance.SaveInternal(name);
        }

        public static void AddOrUpdate(string key, string value)
        {
            Instance.AddOrUpdateInternal(key, value);
        }

        public static bool TryGetValue(string key, out string value)
        {
            return Instance.Cache.TryGetValue(key, out value);
        }

        public static void Remove(string key)
        {
            Instance.RemoveInternal(key);
        }

        public static void Clear()
        {
            Instance.Cache.Clear();
            IsDirty = true;
        }

        public static IEnumerable<KeyValuePair<string, string>> GetAll()
        {
            return Instance.Cache;
        }

        private void LoadInternal(string name)
        {
            var path = Path.Combine(CacheDirectory, $"{name}.xml");
            if (!File.Exists(path)) return;

            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                
                foreach (XmlElement element in doc.DocumentElement.ChildNodes.OfType<XmlElement>())
                {
                    // If attribute "Key" exists, use it (new format), otherwise use element name (old format)
                    string key = element.HasAttribute("Key") 
                        ? DecodeKey(element.GetAttribute("Key")) 
                        : element.Name;

                    if (!string.IsNullOrEmpty(key))
                    {
                        // ModId가 없는 레거시 엔트리는 자동으로 LEGACY_MOD_ID 추가
                        if (key.IndexOf(':') < 0)
                        {
                            key = $"{LEGACY_MOD_ID}:{key}";
                            IsDirty = true; // 변환되었으므로 저장 필요
                        }
                        
                        Cache[key] = element.InnerText;
                    }
                }
            }
            catch (Exception e)
            {
                Log.Error(AutoTranslation.LogPrefix + $"Error loading cache {name}: {e.Message}");
            }
        }

        private void SaveInternal(string name)
        {
            var path = Path.Combine(CacheDirectory, $"{name}.xml");
            var tempPath = Path.Combine(CacheDirectory, $"{name}.tmp");

            try
            {
                var doc = new XmlDocument();
                var root = doc.CreateElement("Cache");
                doc.AppendChild(root);

                root.SetAttribute("Language", LanguageDatabase.activeLanguage?.FriendlyNameEnglish ?? "NULL");

                foreach (var kv in Cache.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;

                    // ModId가 없는 엔트리는 자동으로 LEGACY_MOD_ID 추가
                    string keyToSave = kv.Key;
                    if (keyToSave.IndexOf(':') < 0)
                    {
                        keyToSave = $"{LEGACY_MOD_ID}:{keyToSave}";
                        // 캐시에도 업데이트 (다음번 조회를 위해)
                        Cache.TryRemove(kv.Key, out _);
                        Cache[keyToSave] = kv.Value;
                    }

                    var entry = doc.CreateElement("Entry");
                    // Store original key in attribute to handle special chars safely
                    entry.SetAttribute("Key", EncodeKey(keyToSave));
                    
                    // Extract and store ModId as separate attribute for clarity
                    var colonIndex = keyToSave.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var modId = keyToSave.Substring(0, colonIndex);
                        if (!string.IsNullOrWhiteSpace(modId) && modId.Length < keyToSave.Length / 2)
                        {
                            entry.SetAttribute("ModId", modId);
                        }
                    }
                    
                    entry.InnerText = kv.Value;
                    root.AppendChild(entry);
                }

                doc.Save(tempPath);

                // Safe swap
                if (File.Exists(path)) File.Delete(path);
                File.Move(tempPath, path);
                
                IsDirty = false;
            }
            catch (Exception e)
            {
                Log.Error(AutoTranslation.LogPrefix + $"Error saving cache {name}: {e.Message}");
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        private void AddOrUpdateInternal(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (Cache.TryGetValue(key, out var existing) && existing == value) return;
            
            Cache[key] = value;
            IsDirty = true;
        }

        private void RemoveInternal(string key)
        {
            if (Cache.TryRemove(key, out _))
            {
                IsDirty = true;
            }
        }

        // Encoding helpers for safe XML keys
        private string EncodeKey(string key)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(key));
        }

        private string DecodeKey(string encodedKey)
        {
            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encodedKey));
            }
            catch
            {
                return encodedKey; // Fallback or corrupt?
            }
        }
    }
}
