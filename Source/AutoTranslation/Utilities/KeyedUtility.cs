using System;
using System.Collections.Generic;
using System.IO;
using Verse;

namespace AutoTranslation.Utilities
{
    public static class KeyedUtility
    {
        public static List<KeyedReplacementParams> FindMissingKeyed()
        {
            LoadedLanguage activeLang = LanguageDatabase.activeLanguage, defaultLang = LanguageDatabase.defaultLanguage;
            var res = new List<KeyedReplacementParams>();
            if (activeLang == null || activeLang == defaultLang) return res;

            defaultLang.LoadData();

            foreach (var (k, v) in defaultLang.keyedReplacements)
            {
                if (activeLang.keyedReplacements.ContainsKey(k)) continue;

                res.Add(new KeyedReplacementParams(k, v, FindModContentPackOf(v.fileSourceFullPath)));
            }

            return res;
        }

        public static void AddKeyedToCurrentLanguage(string k, string v)
        {
            var activeLang = LanguageDatabase.activeLanguage;
            if (activeLang == null) return;

            var replacement = new LoadedLanguage.KeyedReplacement
            {
                key = k,
                value = v,
                isPlaceholder = false,
                fileSource = "DynamicallyGeneratedByAutoTranslation",
                fileSourceFullPath = "DynamicallyGeneratedByAutoTranslation",
                fileSourceLine = 1
            };
            activeLang.keyedReplacements.SetOrAdd(k, replacement);
        }

        public static void RemoveKeyedFromCurrentLanguage(string k)
        {
            var activeLang = LanguageDatabase.activeLanguage;

            activeLang?.keyedReplacements.Remove(k);
        }

        public class KeyedReplacementParams
        {
            public string key;
            public string translation = string.Empty;
            public LoadedLanguage.KeyedReplacement value;
            public ModContentPack mod;
            public bool injected = false;

            public KeyedReplacementParams(string key, LoadedLanguage.KeyedReplacement value, ModContentPack mod)
            {
                this.key = key;
                this.value = value;
                this.mod = mod;
            }

            public void Inject()
            {
                if (injected) return;
                AddKeyedToCurrentLanguage(key, translation);
                injected = true;
            }

            public void UndoInject()
            {
                if (!injected) return;
                RemoveKeyedFromCurrentLanguage(key);
                injected = false;
            }
        }

        private static ModContentPack FindModContentPackOf(string path)
        {
            if (modRootDirs == null)
            {
                modRootDirs = new Dictionary<string, ModContentPack>();
                foreach (var modContentPack in LoadedModManager.RunningMods)
                {
                    modRootDirs.SetOrAdd(modContentPack.RootDir, modContentPack);
                }
            }

            try
            {
                while (path != null)
                {
                    if (modRootDirs.TryGetValue(path, out var res)) return res;
                    path = Directory.GetParent(path)?.FullName;
                }
            }
            catch (Exception e)
            {
                var msg = AutoTranslation.LogPrefix + $"FindModContentPackOf failed. {e}";
                Log.WarningOnce(msg, msg.GetHashCode());
            }

            return null;
        }

        private static Dictionary<string, ModContentPack> modRootDirs;
    }
}

