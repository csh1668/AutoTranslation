using Verse;

namespace AutoTranslation.Translators
{
    public class TranslatorSettings_AIModel : TranslatorSettings
    {
        public string UserAPIKey = "";
        public string UserSelectedModel = "";
        public string UserCustomBaseURL = "";
        public string UserCustomPrompt = "";
        
        public override void ExposeData()
        {
            Scribe_Values.Look(ref UserAPIKey, "UserAPIKey");
            Scribe_Values.Look(ref UserSelectedModel, "UserSelectedModel");
            Scribe_Values.Look(ref UserCustomBaseURL, "UserCustomBaseURL");
            Scribe_Values.Look(ref UserCustomPrompt, "UserCustomPrompt");
        }
    }
}


