using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;

namespace AutoTranslation.Translators
{
    public class Translator_Ollama: Translator_BaseAIModel
    {
        public override string Name => "Ollama";
        public override string BaseURL => "http://localhost:11434/v1/";

        protected virtual string RoleSystem => "system";

        public override List<string> GetModels()
        {
            try
            {
                var request = WebRequest.Create($"{RequestURL}models");
                request.Method = "GET";
                if (!string.IsNullOrEmpty(APIKey.Trim()))
                    request.Headers.Add("Authorization", "Bearer " + APIKey);

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
            var requestBody = $@"{{
                ""model"": ""{Model}"",
                ""messages"": [
                  {{
                    ""role"": ""{RoleSystem}"",
                    ""content"": ""{Prompt.EscapeJsonString()}""
                  }},
                  {{
                    ""role"": ""user"",
                    ""content"": ""{text.EscapeJsonString()}""
                  }}
                ]
            }}";

            var request = WebRequest.Create($"{RequestURL}chat/completions");
            request.Method = "POST";
            request.ContentType = "application/json";
            if (!string.IsNullOrEmpty(APIKey.Trim())) request.Headers.Add("Authorization", "Bearer " + APIKey);

            using (var sw = new StreamWriter(request.GetRequestStream()))
            {
                sw.Write(requestBody);
            }

            return request.GetResponseAndReadText();
        }

        protected override string ParseResponse(string response)
        {
            string content = "";
            
            try
            {
                content = response.GetStringValueFromJson("content");
            }
            catch
            {
                try
                {
                    content = response.GetStringValueFromJson("text");
                }
                catch
                {
                    return content;
                }
            }

            if (string.IsNullOrEmpty(content))
                return "";

            content = content.Replace(@"\u003c", "<").Replace(@"\u003e", ">").Replace(@"\u000a", "\n");

            content = System.Text.RegularExpressions.Regex.Replace(content, @"<think>.*?</think>", "", 
                System.Text.RegularExpressions.RegexOptions.Singleline | 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            content = content.Replace(@"\n", " ");
            content = content.Replace(@"\r", " ");
        
            content = System.Text.RegularExpressions.Regex.Replace(content, @"\s+", " ");

            return content.Trim();
        }
    }
}