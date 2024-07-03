using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace AutoTranslation
{
    /// <summary>
    /// TODO: Temporary fixes
    /// because I'm so lazy ;)
    /// </summary>
    public static class SpaghettiCodes
    {
        public static void SpanishPsychology()
        {
            if (ModsConfig.IsActive("community.psychology.unofficialupdate") &&
                LanguageDatabase.activeLanguage?.LegacyFolderName.Contains("Spanish") == true)
            {
                TranslatorManager.CachedTranslations["optimistic10"] = "positividad";
            }
        }
    }
}
