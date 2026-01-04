using Verse;

namespace AutoTranslation.Translators
{
    public class TranslatorSettings_DeepL : TranslatorSettings
    {
        public string APIKey = "";

        public override void ExposeData()
        {
            Scribe_Values.Look(ref APIKey, "APIKey", "");
        }
    }
}

