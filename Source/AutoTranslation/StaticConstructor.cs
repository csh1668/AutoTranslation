using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            var t = string.Empty;
            var flag = TranslatorManager.CurrentTranslator?.TryTranslate("Hello, World!", out t);
            Log.Message(AutoTranslation.LogPrefix + $"Translator test: Hello, World! => {(flag == true ? t : "FAILED!")}");
            Log.Message(AutoTranslation.LogPrefix + $"Elapsed time during loading: {AutoTranslation.sw.ElapsedMilliseconds}ms, untranslated defInjections: {InjectionManager.defInjectedMissing.Count}, untranslated keyeds: {InjectionManager.keyedMissing.Count}");


            //var res = Translator_DeepL.Parse(Translator_DeepL.GetResponseUnsafe("https://api-free.deepl.com/v2/translate", new List<IMultipartFormSection>()
            //{
            //    new MultipartFormDataSection("auth_key", Settings.APIKey),
            //    new MultipartFormDataSection("text", Translator_DeepL.EscapePlaceholders("Hello, {PAWN_NAME}! Welcome to my {0} game")),
            //    //new MultipartFormDataSection("source_lang", "EN"),
            //    new MultipartFormDataSection("target_lang", "KO"),
            //    new MultipartFormDataSection("preserve_formatting", "true"),
            //    new MultipartFormDataSection("tag_handling", "xml"),
            //    new MultipartFormDataSection("ignore_tags", "x")
            //}), out var detectedLang);
            //Log.Message(res);
            //Log.Message(detectedLang);
        }
    }
}
