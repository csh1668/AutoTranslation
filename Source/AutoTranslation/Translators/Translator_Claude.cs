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

namespace AutoTranslation.Translators
{
    public class Translator_Claude : Translator_BaseOnlineAIModel
    {
        public override string Name => "Claude";

        public override string BaseURL => "https://api.anthropic.com/v1/";

        public override List<string> GetModels()
        {
            try
            {
                var headers = new Dictionary<string, string>
                {
                    { "x-api-key", APIKey },
                    { "anthropic-version", AnthropicVersion }
                };

                var raw = NetworkHelper.Get(Helpers.CombineUrl(RequestURL, "models"), headers);
                var models = raw.GetStringValuesFromJson("id");

                return models;
            }
            catch (Exception e)
            {
                // Messages.Message("AT_Message_FailedToGetModels".Translate() + e.Message, MessageTypeDefOf.NegativeEvent);
                return null;
            }
        }

        protected override string GetResponseUnsafe(string text)
        {
            var requestBody = $@"{{
                ""model"": ""{Model}"",
                ""max_tokens"": 1024,
                ""system"": ""{Prompt.EscapeJsonString()}"",
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

            return NetworkHelper.Post(Helpers.CombineUrl(RequestURL, "messages"), requestBody, headers);
        }

        private const string AnthropicVersion = "2023-06-01";
    }
}
