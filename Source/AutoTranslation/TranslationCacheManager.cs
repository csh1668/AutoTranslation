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
                // Separate entries: with ModId and without ModId (old format)
                var entriesWithModId = new List<KeyValuePair<string, string>>();
                var entriesWithoutModId = new List<KeyValuePair<string, string>>();
                
                foreach (var kv in Cache.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    if (kv.Key.IndexOf(':') > 0)
                    {
                        entriesWithModId.Add(kv);
                    }
                    else
                    {
                        entriesWithoutModId.Add(kv);
                    }
                }
                
                // Backup old entries if any exist
                if (entriesWithoutModId.Count > 0)
                {
                    var backupPath = Path.Combine(CacheDirectory, $"{name}_backup_{DateTime.Now:yyyyMMdd_HHmmss}.xml");
                    try
                    {
                        var backupDoc = new XmlDocument();
                        var backupRoot = backupDoc.CreateElement("Cache");
                        backupDoc.AppendChild(backupRoot);
                        backupRoot.SetAttribute("Language", LanguageDatabase.activeLanguage?.FriendlyNameEnglish ?? "NULL");
                        backupRoot.SetAttribute("Note", "Backup of old format entries without ModId");
                        
                        foreach (var kv in entriesWithoutModId)
                        {
                            if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;
                            var entry = backupDoc.CreateElement("Entry");
                            entry.SetAttribute("Key", EncodeKey(kv.Key));
                            entry.InnerText = kv.Value;
                            backupRoot.AppendChild(entry);
                        }
                        
                        backupDoc.Save(backupPath);
                        Log.Message(AutoTranslation.LogPrefix + $"Backed up {entriesWithoutModId.Count} old format entries to {Path.GetFileName(backupPath)}");
                        
                        // Remove old entries from cache
                        foreach (var kv in entriesWithoutModId)
                        {
                            Cache.TryRemove(kv.Key, out _);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(AutoTranslation.LogPrefix + $"Failed to backup old entries: {ex.Message}");
                    }
                }
                
                var doc = new XmlDocument();
                var root = doc.CreateElement("Cache");
                doc.AppendChild(root);

                root.SetAttribute("Language", LanguageDatabase.activeLanguage?.FriendlyNameEnglish ?? "NULL");

                foreach (var kv in entriesWithModId)
                {
                    if (string.IsNullOrEmpty(kv.Key) || string.IsNullOrEmpty(kv.Value)) continue;

                    var entry = doc.CreateElement("Entry");
                    // Store original key in attribute to handle special chars safely
                    entry.SetAttribute("Key", EncodeKey(kv.Key));
                    
                    // Extract and store ModId as separate attribute for clarity
                    var colonIndex = kv.Key.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var modId = kv.Key.Substring(0, colonIndex);
                        if (!string.IsNullOrWhiteSpace(modId) && modId.Length < kv.Key.Length / 2)
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
