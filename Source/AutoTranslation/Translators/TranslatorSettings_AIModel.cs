using Verse;

namespace AutoTranslation.Translators
{
    public class TranslatorSettings_AIModel : TranslatorSettings
    {
        public string UserAPIKey = "";
        public string UserSelectedModel = "";
        public string UserCustomBaseURL = "";
        public string UserCustomPrompt = "";
        
        // Batch translation settings
        public bool EnableBatchTranslation = true;
        public int BatchSizeTokens = 2000;
        
        public override void ExposeData()
        {
            Scribe_Values.Look(ref UserAPIKey, "UserAPIKey");
            Scribe_Values.Look(ref UserSelectedModel, "UserSelectedModel");
            Scribe_Values.Look(ref UserCustomBaseURL, "UserCustomBaseURL");
            Scribe_Values.Look(ref UserCustomPrompt, "UserCustomPrompt");
            Scribe_Values.Look(ref EnableBatchTranslation, "EnableBatchTranslation", true);
            Scribe_Values.Look(ref BatchSizeTokens, "BatchSizeTokens", 2000);
        }
    }
}


