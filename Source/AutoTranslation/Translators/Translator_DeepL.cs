using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Diagnostics;
using UnityEngine.Networking;
using Verse;

namespace AutoTranslation.Translators
{
    internal class Translator_DeepL : ITranslator
    {
        private static readonly StringBuilder sb = new StringBuilder(1024);

        public string Name => "DeepL";
        public bool Ready { get; private set; }
        public bool RequiresKey => true;

        public void Prepare()
        {
            if (string.IsNullOrEmpty(Settings.APIKey))
                return;
            Ready = true;
        }

        public bool TryTranslate(string text, out string translated)
        {
            try
            {
                string url = $"https://api-free.deepl.com/v2/translate";


                var request = UnityWebRequest.Post(url, new List<IMultipartFormSection>()
                {
                    new MultipartFormDataSection("auth_key", Settings.APIKey),
                    new MultipartFormDataSection("text", text),
                    //new MultipartFormDataSection("source_lang", "EN"),
                    new MultipartFormDataSection("target_lang", "KO"),
                    new MultipartFormDataSection("preserve_formatting", "true")
                });

                var asyncOperation = request.SendWebRequest();
                while (!asyncOperation.isDone)
                {
                    Task.Delay(1);
                }

                if (request.isNetworkError || request.isHttpError)
                {
                    throw new Exception("Web error");
                }

                translated = Parse(request.downloadHandler.text);
                return true;
            }
            catch (Exception e)
            {
                var msg = AutoTranslation.LogPrefix + $"{Name}, translate failed. reason: {e.Message}";
                Log.WarningOnce(msg, msg.GetHashCode());
            }

            translated = text;
            return false;
        }

        private static string Parse(string text)
        {
            var textKey = "\"text\":\"";
            var startIdx = text.IndexOf(textKey, StringComparison.Ordinal) + textKey.Length;
            var endIdx = text.LastIndexOf('\"');
            return text.Substring(startIdx, endIdx - startIdx);
        }

        private static string GetTranslateLanguage()
        {
            if (LanguageDatabase.activeLanguage == null)
            {
                Log.Warning(AutoTranslation.LogPrefix + "activeLanguage was null");
                return "en";
            }

            switch (LanguageDatabase.activeLanguage.FriendlyNameEnglish)
            {
                case "Korean": return "KO";
                //case "Catalan": return "ca";
                case "Simplified Chinese": return "ZH";
                //case "Traditional Chinese": return "zh-TW";
                case "Czech": return "CS";
                case "Danish": return "DA";
                case "Dutch": return "NL";
                case "Estonian": return "ET";
                case "Finnish": return "FI";
                case "French": return "FR";
                case "German": return "DE";
                case "Greek": return "EL";
                case "Hungarian": return "HU";
                case "Italian": return "IT";
                case "Japanese": return "JA";
                case "Norwegian": return "NB";
                case "Polish": return "PL";
                case "Portuguese": return "PT-PT";
                case "Brazilian Portuguese": return "PT-BR";
                case "Romanian": return "RO";
                case "Russian": return "RU";
                case "Slovak": return "SK";
                case "Latin American Spanish":
                case "Spanish": return "ES";
                case "Swedish": return "SV";
                case "Turkish": return "TR";
                case "Ukrainian": return "UK";
                case "English": return "EN";
                default:
                    Log.Error(AutoTranslation.LogPrefix +
                                $"Unsupported language: {LanguageDatabase.activeLanguage.FriendlyNameEnglish}, Please change the Translator.");
                    return "en";
            }
        }
    }
}
