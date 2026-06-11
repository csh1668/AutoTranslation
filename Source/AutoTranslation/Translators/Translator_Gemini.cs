using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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
            var key = APIKey; // capture once: APIKey rotates per access, pagination must use one key
            var models = new List<string>();
            string pageToken = null;

            // Default pageSize is 50 and there are now well over 50 models,
            // so pagination is required to avoid silently missing models.
            do
            {
                var url = Helpers.CombineUrl(RequestURL, "models") + $"?key={key}&pageSize=1000";
                if (!string.IsNullOrEmpty(pageToken))
                {
                    url += $"&pageToken={pageToken}";
                }

                var raw = NetworkHelper.Get(url, timeoutMs: TimeoutMs);
                models.AddRange(ParseGenerateContentModels(raw));
                pageToken = raw.GetStringValueFromJson("nextPageToken");
            } while (!string.IsNullOrEmpty(pageToken));

            return models;
        }

        /// <summary>
        /// Extracts model names that support generateContent (filters out embedding/aqa models).
        /// Splits the JSON into per-model segments on "name" keys; each segment holds that
        /// model's fields including supportedGenerationMethods.
        /// </summary>
        private static List<string> ParseGenerateContentModels(string json)
        {
            var result = new List<string>();
            var segments = json.Split(new[] { "\"name\"" }, StringSplitOptions.None);

            for (int i = 1; i < segments.Length; i++)
            {
                var match = Regex.Match(segments[i], "^\\s*:\\s*\"models/([^\"]+)\"");
                if (!match.Success) continue;

                if (segments[i].Contains("\"generateContent\""))
                {
                    result.Add(match.Groups[1].Value);
                }
            }

            return result;
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

            return NetworkHelper.Post(url, requestBody, timeoutMs: TimeoutMs);
        }

        protected override string ParseResponse(string response)
        {
            var ret = base.ParseResponse(response);
            if (ret != null && ret.EndsWith("\\n")) ret = ret.Substring(0, ret.Length - 2);
            return ret;
        }
    }
}
