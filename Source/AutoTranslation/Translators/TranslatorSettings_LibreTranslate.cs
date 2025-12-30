using Verse;

namespace AutoTranslation.Translators
{
    public class TranslatorSettings_LibreTranslate : TranslatorSettings
    {
        public string CustomUrl = "https://libretranslate.com";
        public string APIKey = "";

        public override void ExposeData()
        {
            Scribe_Values.Look(ref CustomUrl, "CustomUrl", "https://libretranslate.com");
            Scribe_Values.Look(ref APIKey, "APIKey", "");
        }
    }
}


