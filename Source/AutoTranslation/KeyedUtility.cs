using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace AutoTranslation
{
    public static class KeyedUtility
    {
        public static List<(string, string)> FindMissingKeyed()
        {
            LoadedLanguage activeLang = LanguageDatabase.activeLanguage, defaultLang = LanguageDatabase.defaultLanguage;
            var res = new List<(string, string)>();
            if (activeLang == null || activeLang == defaultLang) return res;

            defaultLang.LoadData();

            foreach (var (k, v) in defaultLang.keyedReplacements)
            {
                if (activeLang.keyedReplacements.ContainsKey(k)) continue;

                res.Add((k, v.value));
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
    }
}
