using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
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

            // /v1/models is paginated (default limit 20) - follow has_more/last_id
            // so models beyond the first page are not silently missed
            var models = new List<string>();
            string afterId = null;

            do
            {
                var url = Helpers.CombineUrl(RequestURL, "models") + "?limit=100";
                if (!string.IsNullOrEmpty(afterId))
                {
                    url += $"&after_id={afterId}";
                }

                // Exceptions propagate: the base class extracts the API error for the settings UI
                var raw = NetworkHelper.Get(url, headers, timeoutMs: TimeoutMs);
                models.AddRange(raw.GetStringValuesFromJson("id"));

                var hasMore = Regex.IsMatch(raw, "\"has_more\"\\s*:\\s*true");
                afterId = hasMore ? raw.GetStringValueFromJson("last_id") : null;
            } while (!string.IsNullOrEmpty(afterId));

            return models;
        }

        protected override string GetResponseUnsafe(string text, string prompt)
        {
            // Estimate if this is a batch request based on text length
            // Batch requests need higher token limits
            // 4096/8192 are supported by every current Claude model (only the retired
            // claude-3-haiku capped at 4096; anything newer allows at least 8192)
            var isBatchRequest = text.Contains("<translations>") && text.Contains("</translations>");
            var maxTokens = isBatchRequest ? 8192 : 4096;
            
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
