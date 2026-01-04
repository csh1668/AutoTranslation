using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;
using AutoTranslation;
using AutoTranslation.Utilities;

namespace AutoTranslation.Translators
{
    public class Translator_Gemini : Translator_BaseOnlineAIModel
    {
        public override string Name => "Gemini";

        public override string BaseURL => "https://generativelanguage.googleapis.com/v1beta/";

        public override List<string> GetModels()
        {
            try
            {
                var key = APIKey;
                var url = Helpers.CombineUrl(RequestURL, "models") + $"?key={key}";
                
                var raw = NetworkHelper.Get(url);

                var models = raw.GetStringValuesFromJson("name").Select(n => n.Split('/').Last()).ToList();

                return models;
            }
            catch (Exception)
            {
                // Silently return null, base class will handle logging
                return null;
            }
        }

        protected override string GetResponseUnsafe(string text, string prompt)
        {
            var url = Helpers.CombineUrl(RequestURL, "models", $"{Model}:generateContent") + $"?key={APIKey}";
            
            // Estimate if this is a batch request based on text length
            // Batch requests need higher token limits
            var isBatchRequest = text.Contains("<translations>") && text.Contains("</translations>");
            var maxOutputTokens = isBatchRequest ? 8192 : 2048;
            
            var requestBody = $@"{{
	            ""contents"": [
		            {{
			            ""parts"": [
				            {{
					            ""text"": ""{text.EscapeJsonString()}""
				            }}
			            ]
		            }}
	            ],
	            ""systemInstruction"": {{
		            ""parts"": [
			            {{
				            ""text"": ""{prompt.EscapeJsonString()}""
			            }}
		            ]
	            }},
	            ""generationConfig"": {{
		            ""maxOutputTokens"": {maxOutputTokens},
		            ""temperature"": 0.3
	            }}
            }}";

            return NetworkHelper.Post(url, requestBody);
        }

        protected override string ParseResponse(string response)
        {
            var ret = base.ParseResponse(response);
            if (ret != null && ret.EndsWith("\\n")) ret = ret.Substring(0, ret.Length - 2);
            return ret;
        }
    }
}
