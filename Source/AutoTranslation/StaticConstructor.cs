using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AutoTranslation.Services;
using AutoTranslation.Translators;
using UnityEngine.Networking;
using Verse;
using static System.Net.Mime.MediaTypeNames;

namespace AutoTranslation
{
    [StaticConstructorOnStartup]
    public static class StaticConstructor
    {
        static StaticConstructor()
        {
            // No startup test translation: it ran a synchronous network request on the main
            // thread during loading and could block for minutes when the endpoint hangs
            Log.Message(AutoTranslation.LogPrefix + $"Elapsed time during loading: {AutoTranslation.sw.ElapsedMilliseconds}ms, untranslated defInjections: {InjectionManager.defInjectedMissing.Count}, untranslated keyeds: {InjectionManager.keyedMissing.Count}");
        }
    }
}
