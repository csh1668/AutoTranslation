using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using RimWorld;
using Verse;
using static System.Net.Mime.MediaTypeNames;

namespace AutoTranslation.Translators
{
    public class Translator_Gemini : Translator_BaseAIModel
    {
        public override string Name => "Gemini";

        public override string BaseURL => "https://generativelanguage.googleapis.com/v1beta/";

        public override List<string> GetModels()
        {
            try
            {
                var key = APIKey;
                //var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={key}";
                var url = $"{RequestURL}models?key={key}";
                var request = WebRequest.Create(url);
                request.Method = "GET";

                var raw = request.GetResponseAndReadText();

                var models = raw.GetStringValuesFromJson("name").Select(n => n.Split('/').Last()).ToList();

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
            //var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={APIKey}";
            var url = $"{RequestURL}models/{Model}:generateContent?key={APIKey}";
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
				            ""text"": ""{Prompt.EscapeJsonString()}""
			            }}
		            ]
	            }}
            }}";


            var request = WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";

            using (var sw = new StreamWriter(request.GetRequestStream()))
            {
                sw.Write(requestBody);
            }

            return request.GetResponseAndReadText();
        }

        protected override string ParseResponse(string response)
        {
            var ret = base.ParseResponse(response);
            if (ret.EndsWith("\\n")) ret = ret.Substring(0, ret.Length - 2);
            return ret;
        }
    }
}
