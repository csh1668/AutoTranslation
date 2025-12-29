using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace AutoTranslation.Translators
{
    public class Translator_Claude : Translator_BaseAIModel
    {
        public override string Name => "Claude";

        public override string BaseURL => "https://api.anthropic.com/v1/";

        public override List<string> GetModels()
        {
            try
            {
                //var url = "https://api.anthropic.com/v1/models";
                var url = $"{RequestURL}models";
                var request = WebRequest.Create(url);
                request.Method = "GET";
                request.Headers.Add("x-api-key", APIKey);
                request.Headers.Add("anthropic-version", AnthropicVersion);

                var raw = request.GetResponseAndReadText();
                var models = raw.GetStringValuesFromJson("id");

                return models;
            }
            catch (Exception e)
            {
                Messages.Message("AT_Message_FailedToGetModels".Translate() + e.Message, MessageTypeDefOf.NegativeEvent);
                return null;
            }
        }

        protected override string GetResponseUnsafe(string text)
        {
            //var url = "https://api.anthropic.com/v1/messages";
            var url = $"{RequestURL}messages";
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

            var request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers.Add("x-api-key", APIKey);
            request.Headers.Add("anthropic-version", AnthropicVersion);

            using (var sw = new StreamWriter(request.GetRequestStream()))
            {
                sw.Write(requestBody);
            }

            return request.GetResponseAndReadText();
        }

        private const string AnthropicVersion = "2023-06-01";
    }
}
