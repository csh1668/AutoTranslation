using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Diagnostics;
using UnityEngine.Networking;
using Verse;

namespace AutoTranslation.Translators
{
    internal class Translator_DeepL_Pro : Translator_DeepL
    {
        protected override string url => $"https://api.deepl.com/v2/translate";

        public override string Name => "DeepL (Pro)";
    }
}
