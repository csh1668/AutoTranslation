using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using AutoTranslation;

namespace AutoTranslation.Translators
{
    public class Translator_OpenAICompatible : Translator_BaseOnlineAIModel
    {
        public override string Name => "OpenAI Compatible (Local/Other)";
        
        // Default to local generic, but user can change in settings
        public override string BaseURL => "http://localhost:11434/v1/"; 

        public override bool RequiresKey => false; // Often not needed for local

        public override List<string> GetModels()
        {
            try
            {
                var url = Helpers.CombineUrl(RequestURL, "models");
                var headers = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(APIKey))
                {
                    headers.Add("Authorization", "Bearer " + APIKey);
                }

                var raw = NetworkHelper.Get(url, headers);
                
                // Try standard OpenAI format
                var models = raw.GetStringValuesFromJson("id");
                
                // Fallback for some local servers that might return just a list
                if (models == null || models.Count == 0)
                {
                    models = raw.GetStringValuesFromJson("name");
                }

                return models ?? new List<string>();
            }
            catch (System.Exception)
            {
                // Silently return empty list, base class will handle logging
                return new List<string>();
            }
        }

        public override void Prepare()
        {
            base.Prepare();
            // OpenAI Compatible doesn't STRICTLY require key, so Ready is true unless base logic says otherwise
            // Base logic only returns if RequiresKey is true AND key is empty.
            // We set RequiresKey => false, so base Prepare is fine.
            Ready = true;
        }

        protected override string GetResponseUnsafe(string text, string prompt)
        {
            var url = Helpers.CombineUrl(RequestURL, "chat", "completions");
            
            // Standard OpenAI Chat Completion Body
            var requestBody = $@"{{
                ""model"": ""{Model ?? "default"}"",
                ""messages"": [
                  {{
                    ""role"": ""system"",
                    ""content"": ""{prompt.EscapeJsonString()}""
                  }},
                  {{
                    ""role"": ""user"",
                    ""content"": ""{text.EscapeJsonString()}""
                  }}
                ],
                ""temperature"": 0.3
            }}";

            var headers = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(APIKey))
            {
                headers.Add("Authorization", "Bearer " + APIKey);
            }

            var response = NetworkHelper.Post(url, requestBody, headers);
            return response;
        }

        protected override string ParseResponse(string response)
        {
            // Standard OpenAI response
            var content = response.GetStringValueFromJson("content");
            return content?.Trim() ?? response; // Fallback to raw if parse fails
        }
    }
}
