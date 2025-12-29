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
            catch (Exception e)
            {
                // Messages.Message("AT_Message_FailedToGetModels".Translate() + e.Message, MessageTypeDefOf.NegativeEvent);
                return null;
            }
        }

        protected override string GetResponseUnsafe(string text)
        {
            var url = Helpers.CombineUrl(RequestURL, "models", $"{Model}:generateContent") + $"?key={APIKey}";
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
