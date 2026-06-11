using System;
using System.Collections.Generic;
using RimWorld;
using Verse;
using AutoTranslation;
using AutoTranslation.Utilities;

namespace AutoTranslation.Translators
{
    public class Translator_ChatGPT : Translator_BaseOnlineAIModel
    {
        public override string Name => "ChatGPT";
        public override string BaseURL => "https://api.openai.com/v1/";

        protected virtual string RoleSystem => "system";

        public override List<string> GetModels()
        {
            try
            {
                var headers = new Dictionary<string, string>
                {
                    { "Authorization", "Bearer " + APIKey }
                };

                var raw = NetworkHelper.Get(Helpers.CombineUrl(RequestURL, "models"), headers, timeoutMs: TimeoutMs);
                var models = raw.GetStringValuesFromJson("id");

                return models;
            }
            catch (Exception e)
            {
                // Messages.Message("AT_Message_FailedToGetModels".Translate() + e.Message, MessageTypeDefOf.NegativeEvent);
                return null;
            }
        }

        protected override string GetResponseUnsafe(string text, string prompt)
        {
            var requestBody = $@"{{
                ""model"": ""{Model}"",
                ""messages"": [
                  {{
                    ""role"": ""{RoleSystem}"",
                    ""content"": ""{prompt.EscapeJsonString()}""
                  }},
                  {{
                    ""role"": ""user"",
                    ""content"": ""{text.EscapeJsonString()}""
                  }}
                ]
            }}";

            var headers = new Dictionary<string, string>
            {
                { "Content-Type", "application/json" },
                { "Authorization", "Bearer " + APIKey }
            };

            return NetworkHelper.Post(Helpers.CombineUrl(RequestURL, "chat", "completions"), requestBody, headers, timeoutMs: TimeoutMs);
        }

        protected override string ParseResponse(string response)
        {
            return response.GetStringValueFromJson("content")?.Trim() ?? response;
        }
    }
}
