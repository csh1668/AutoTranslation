using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Verse;
using AutoTranslation;
using AutoTranslation.Utilities;

namespace AutoTranslation.Translators
{
    public class Translator_Claude : Translator_BaseOnlineAIModel
    {
        public override string Name => "Claude";

        public override string BaseURL => "https://api.anthropic.com/v1/";

        public override List<string> GetModels()
        {
            var headers = new Dictionary<string, string>
            {
                { "x-api-key", APIKey },
                { "anthropic-version", AnthropicVersion }
            };

            // Exceptions propagate: the base class extracts the API error for the settings UI
            var raw = NetworkHelper.Get(Helpers.CombineUrl(RequestURL, "models"), headers, timeoutMs: TimeoutMs);
            return raw.GetStringValuesFromJson("id");
        }

        protected override string GetResponseUnsafe(string text, string prompt)
        {
            // Estimate if this is a batch request based on text length
            // Batch requests need higher token limits
            var isBatchRequest = text.Contains("<translations>") && text.Contains("</translations>");
            var maxTokens = isBatchRequest ? 4096 : 1024;
            
            var requestBody = $@"{{
                ""model"": ""{Model}"",
                ""max_tokens"": {maxTokens},
                ""system"": ""{prompt.EscapeJsonString()}"",
                ""messages"": [
                    {{
                        ""role"": ""user"",
                        ""content"": ""{text.EscapeJsonString()}""
                    }}
                ]
            }}";

            var headers = new Dictionary<string, string>
            {
                { "x-api-key", APIKey },
                { "anthropic-version", AnthropicVersion }
            };

            return NetworkHelper.Post(Helpers.CombineUrl(RequestURL, "messages"), requestBody, headers, timeoutMs: TimeoutMs);
        }

        private const string AnthropicVersion = "2023-06-01";
    }
}
