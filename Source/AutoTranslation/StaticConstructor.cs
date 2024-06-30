using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace AutoTranslation
{
    [StaticConstructorOnStartup]
    public static class StaticConstructor
    {
        
        static StaticConstructor()
        {
            var t = string.Empty;
            var flag = TranslatorManager.CurrentTranslator?.TryTranslate("Hello, World!", out t);
            Log.Message(AutoTranslation.LogPrefix + $"Translator test: Hello, World! => {(flag == true ? t : "FAILED!")}");
            Log.Message(AutoTranslation.LogPrefix + $"Elapsed time during loading: {AutoTranslation.sw.ElapsedMilliseconds}ms, untranslated defInjections: {Patches.defInjectedMissing.Count}, untranslated keyeds: {Patches.keyedMissing.Count}");
        }
    }
}
