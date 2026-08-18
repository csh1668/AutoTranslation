using Verse;

namespace AutoTranslation.Translators
{
    public class TranslatorSettings_DeepL : TranslatorSettings
    {
        public string APIKey = "";

        // Cost tracking: user-entered price (USD per 1M characters, default 0 = untracked)
        public float PricePerMChars = 0f;
        public long UsageCharacters = 0;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref APIKey, "APIKey", "");
            Scribe_Values.Look(ref PricePerMChars, "PricePerMChars", 0f);
            Scribe_Values.Look(ref UsageCharacters, "UsageCharacters", 0);
        }
    }
}
