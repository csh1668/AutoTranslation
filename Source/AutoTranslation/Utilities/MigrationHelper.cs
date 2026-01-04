using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using AutoTranslation.Services;
using AutoTranslation.Translators;
using Verse;

namespace AutoTranslation.Utilities
{
    /// <summary>
    /// Handles migration from old versions to maintain compatibility with existing user data
    /// </summary>
    public static class MigrationHelper
    {
        private static bool _migrationCompleted = false;

        public static void PerformMigration()
        {
            if (_migrationCompleted) return;

            try
            {
                Log.Message(AutoTranslation.LogPrefix + "Starting migration check...");

                // Migrate cache files from old format to new format
                MigrateCacheFiles();

                // Legacy settings migration is handled in Settings.ExposeData

                _migrationCompleted = true;
                Log.Message(AutoTranslation.LogPrefix + "Migration completed successfully.");
            }
            catch (Exception e)
            {
                Log.Error(AutoTranslation.LogPrefix + $"Error during migration: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// Migrates old cache format (element name as key) to new format (Base64 key in attribute)
        /// </summary>
        private static void MigrateCacheFiles()
        {
            var cacheDir = TranslationCacheManager.Instance.CacheDirectory;
            if (!Directory.Exists(cacheDir)) return;

            var cacheFiles = Directory.GetFiles(cacheDir, "*.xml");
            foreach (var filePath in cacheFiles)
            {
                try
                {
                    var fileName = Path.GetFileNameWithoutExtension(filePath);
                    
                    // Check if file needs migration
                    if (NeedsMigration(filePath))
                    {
                        Log.Message(AutoTranslation.LogPrefix + $"Migrating cache file: {fileName}");
                        MigrateCacheFile(filePath);
                    }
                }
                catch (Exception e)
                {
                    Log.Warning(AutoTranslation.LogPrefix + $"Failed to migrate {Path.GetFileName(filePath)}: {e.Message}");
                }
            }
        }

        private static bool NeedsMigration(string filePath)
        {
            try
            {
                var doc = new XmlDocument();
                doc.Load(filePath);

                // Check first child element
                var firstChild = doc.DocumentElement?.FirstChild as XmlElement;
                if (firstChild == null) return false;

                // Old format: element name is the key, no "Key" attribute
                // New format: element name is "Entry", has "Key" attribute
                return firstChild.Name != "Entry" || !firstChild.HasAttribute("Key");
            }
            catch
            {
                return false;
            }
        }

        private static void MigrateCacheFile(string filePath)
        {
            var oldData = new Dictionary<string, string>();

            try
            {
                // Read old format
                var doc = new XmlDocument();
                doc.Load(filePath);

                foreach (XmlElement element in doc.DocumentElement.ChildNodes)
                {
                    if (element != null && !string.IsNullOrEmpty(element.Name))
                    {
                        oldData[element.Name] = element.InnerText;
                    }
                }

                if (oldData.Count == 0) return;

                // Create backup
                var backupPath = filePath + ".backup";
                if (!File.Exists(backupPath))
                {
                    File.Copy(filePath, backupPath, false);
                }

                // Load into new cache manager
                foreach (var kvp in oldData)
                {
                    TranslationCacheManager.AddOrUpdate(kvp.Key, kvp.Value);
                }

                // Save in new format
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                TranslationCacheManager.Save(fileName);

                Log.Message(AutoTranslation.LogPrefix + $"Successfully migrated {oldData.Count} entries from {fileName}");
            }
            catch (Exception e)
            {
                Log.Error(AutoTranslation.LogPrefix + $"Failed to migrate cache file: {e.Message}");
            }
        }

        /// <summary>
        /// Migrates legacy translator settings to new polymorphic structure
        /// </summary>
        public static void MigrateLegacyTranslatorSettings(
            string legacyAPIKey,
            string legacySelectedModel,
            string legacyCustomBaseURL,
            string legacyCustomPrompt)
        {
            // Only migrate if we have legacy data and no new data
            if (string.IsNullOrEmpty(legacyAPIKey) && 
                string.IsNullOrEmpty(legacySelectedModel) && 
                string.IsNullOrEmpty(legacyCustomBaseURL) && 
                string.IsNullOrEmpty(legacyCustomPrompt))
            {
                return;
            }

            // Check if we already have new format settings
            if (Settings.TranslatorSettings != null && Settings.TranslatorSettings.Count > 0)
            {
                return; // Already migrated or new installation
            }

            Log.Message(AutoTranslation.LogPrefix + "Migrating legacy translator settings...");

            // Migrate to AI model translators that use these settings
            var aiTranslators = new[] { "ChatGPT", "Claude", "Gemini", "DeepSeek", "Ollama", "GenericOpenAI" };

            foreach (var translatorName in aiTranslators)
            {
                if (!Settings.TranslatorSettings.ContainsKey(translatorName))
                {
                    var aiSettings = new TranslatorSettings_AIModel
                    {
                        UserAPIKey = legacyAPIKey ?? "",
                        UserSelectedModel = legacySelectedModel ?? "",
                        UserCustomBaseURL = legacyCustomBaseURL ?? "",
                        UserCustomPrompt = legacyCustomPrompt ?? ""
                    };
                    
                    Settings.TranslatorSettings[translatorName] = aiSettings;
                }
            }

            Log.Message(AutoTranslation.LogPrefix + "Legacy settings migrated successfully.");
        }
    }
}

