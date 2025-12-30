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
        
        public override string BaseURL => "http://localhost:11434/"; 

        public override bool RequiresKey => false; // Often not needed for local

        public override List<string> GetModels()
        {
            try
            {
                var headers = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(APIKey))
                {
                    headers.Add("Authorization", "Bearer " + APIKey);
                }

                try
                {
                    var openaiUrl = Helpers.CombineUrl(RequestURL, "v1", "models");
                    var raw = NetworkHelper.Get(openaiUrl, headers);
                    
                    // Standard OpenAI format: {"data": [{"id": "model-name"}]}
                    var models = raw.GetStringValuesFromJson("id");
                    
                    if (models != null && models.Count > 0)
                    {
                        return models;
                    }
                }
                catch
                {
                    // OpenAI endpoint failed, try Ollama endpoint
                }

                // Try Ollama endpoint (Ollama-specific)
                try
                {
                    var ollamaUrl = Helpers.CombineUrl(RequestURL, "api", "tags");
                    var raw = NetworkHelper.Get(ollamaUrl, headers);
                    
                    // Ollama format: {"models": [{"name": "llama2:latest"}]}
                    var models = raw.GetStringValuesFromJson("name");
                    
                    if (models != null && models.Count > 0)
                    {
                        return models;
                    }
                }
                catch
                {
                    // Both endpoints failed
                }

                return new List<string>();
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
            // Use standard OpenAI Chat Completions endpoint (supported by LM Studio, Ollama, etc.)
            var url = Helpers.CombineUrl(RequestURL, "v1", "chat", "completions");
            
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
