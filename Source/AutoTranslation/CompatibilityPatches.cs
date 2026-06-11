using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoTranslation.Services;
using Verse;

namespace AutoTranslation
{
    public static class CompatibilityPatches
    {
        public static void ApplyPatches()
        {
            SpanishPsychology();
        }

        private static void SpanishPsychology()
        {
            if (ModsConfig.IsActive("community.psychology.unofficialupdate") &&
                LanguageDatabase.activeLanguage?.LegacyFolderName.Contains("Spanish") == true)
            {
                TranslatorManager.CachedTranslationsV2["optimistic10"] = "positividad";
            }
        }
    }
}






