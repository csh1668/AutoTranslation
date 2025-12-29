/*
Original code: https://github.com/HIllya51/LunaTranslator/blob/main/src/LunaTranslator/translator/papago.py
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using RimWorld;
using UnityEngine.Networking;
using Verse;

namespace AutoTranslation.Translators
{
    public class Translator_Papago : Translator_BaseTraditional
    {
        public override string Name => "Papago (Naver)";
        public override bool RequiresKey => false;

        private string _authKey;
        private string _cachedTranslateLanguage;
        private string _cachedStartLanguage;

        public override string StartLanguage => _cachedStartLanguage ?? (_cachedStartLanguage = GetStartLanguage());
        public override string TranslateLanguage => _cachedTranslateLanguage ?? (_cachedTranslateLanguage = GetTranslateLanguage());

        public override void Prepare()
        {
            try
            {
                Log.Message(AutoTranslation.LogPrefix + "Papago: Initializing...");

                // Get main page to extract JavaScript URL
                var headers = new Dictionary<string, string>
                {
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" },
                    { "Accept", "text/html,application/xhtml+xml,application/xml;q=0.9" }
                };

                Log.Message(AutoTranslation.LogPrefix + "Papago: Fetching main page...");
                var hostHtml = NetworkHelper.Get("https://papago.naver.com/", headers, maxRetries: 1); // Reduce retries for faster failure
                
                // Extract main.*.chunk.js URL
                var urlPathMatch = Regex.Match(hostHtml, @"/main\.(.*?)\.chunk\.js");
                if (!urlPathMatch.Success)
                {
                    throw new Exception("Failed to extract chunk.js URL from main page");
                }

                var languageUrl = "https://papago.naver.com" + urlPathMatch.Value;
                
                // Get JavaScript file to extract auth key
                Log.Message(AutoTranslation.LogPrefix + $"Papago: Fetching auth key from {urlPathMatch.Value}...");
                var langHtml = NetworkHelper.Get(languageUrl, headers, maxRetries: 1);
                
                // Extract auth key: "PPG "(.*)"(.*?)"\).toString
                var authKeyMatch = Regex.Match(langHtml, @"""PPG ""(.*?)""(.*?)""\)\.toString");
                if (!authKeyMatch.Success || authKeyMatch.Groups.Count < 3)
                {
                    throw new Exception("Failed to extract auth key from JavaScript file");
                }

                _authKey = authKeyMatch.Groups[2].Value;
                
                if (string.IsNullOrEmpty(_authKey))
                {
                    throw new Exception("Auth key is empty");
                }

                Ready = true;
                Log.Message(AutoTranslation.LogPrefix + "Papago initialized successfully.");
            }
            catch (System.Net.WebException webEx)
            {
                Log.Error(AutoTranslation.LogPrefix + $"Papago initialization failed (Network): {webEx.Message}. Check your internet connection or try again later.");
                Ready = false;
            }
            catch (Exception ex)
            {
                Log.Error(AutoTranslation.LogPrefix + $"Papago initialization failed: {ex.Message}");
                Ready = false;
            }
        }

        private string GetAuthorizationHeader(string url, string timestamp, string deviceId)
        {
            // Create HMAC-MD5: deviceId + "\n" + url + "\n" + timestamp
            var message = $"{deviceId}\n{url}\n{timestamp}";
            
            using (var hmac = new HMACMD5(Encoding.UTF8.GetBytes(_authKey)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
                var base64Hash = Convert.ToBase64String(hash);
                return $"PPG {deviceId}:{base64Hash}";
            }
        }

        public override bool TryTranslate(string text, out string translated)
        {
            translated = text;
            
            if (!Ready)
            {
                Log.Warning(AutoTranslation.LogPrefix + "Papago is not ready");
                return false;
            }

            try
            {
                // Protect placeholders
                var (protectedText, placeholders) = text.ProtectPlaceholders();
                
                // Generate new device ID for each request
                var deviceId = Guid.NewGuid().ToString();
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
                var apiUrl = "https://papago.naver.com/apis/n2mt/translate";
                
                var headers = new Dictionary<string, string>
                {
                    { "Authorization", GetAuthorizationHeader(apiUrl, timestamp, deviceId) },
                    { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36" },
                    { "Accept", "application/json" },
                    { "Referer", "https://papago.naver.com/" },
                    { "device-type", "pc" },
                    { "timestamp", timestamp },
                    { "x-apigw-partnerid", "papago" }
                };

                // Build form data
                var formData = new StringBuilder();
                formData.Append($"deviceId={UnityWebRequest.EscapeURL(deviceId)}");
                formData.Append($"&locale={UnityWebRequest.EscapeURL(TranslateLanguage)}");
                formData.Append("&dict=true");
                formData.Append("&dictDisplay=30");
                formData.Append("&honorific=false");
                formData.Append("&instant=false");
                formData.Append("&paging=false");
                formData.Append($"&source={UnityWebRequest.EscapeURL(StartLanguage)}");
                formData.Append($"&target={UnityWebRequest.EscapeURL(TranslateLanguage)}");
                formData.Append($"&text={UnityWebRequest.EscapeURL(protectedText)}");


                var response = NetworkHelper.Post(apiUrl, formData.ToString(), headers, "application/x-www-form-urlencoded");
                
                // Parse JSON response
                var translatedText = response.GetStringValueFromJson("translatedText");
                
                if (string.IsNullOrEmpty(translatedText))
                {
                    throw new Exception("Translation result is empty");
                }

                // Restore placeholders
                var (restoredText, allRestored) = translatedText.RestorePlaceholders(placeholders);
                
                if (!allRestored)
                {
                    Log.Warning(AutoTranslation.LogPrefix + $"Papago: Some placeholders were not properly restored");
                    translated = text;
                    return false;
                }

                translated = restoredText;
                return true;
            }
            catch (System.Net.WebException webEx)
            {
                var response = webEx.Response as System.Net.HttpWebResponse;
                if (response != null)
                {
                    using (var stream = response.GetResponseStream())
                    using (var reader = new System.IO.StreamReader(stream))
                    {
                        var errorBody = reader.ReadToEnd();
                        var msg = AutoTranslation.LogPrefix + $"Papago translation failed: {webEx.Message}\nResponse: {errorBody}";
                        Log.WarningOnce(msg + $", target: {text}", msg.GetHashCode());
                    }
                }
                else
                {
                    var msg = AutoTranslation.LogPrefix + $"Papago translation failed: {webEx.Message}";
                    Log.WarningOnce(msg + $", target: {text}", msg.GetHashCode());
                }
                translated = text;
                return false;
            }
            catch (Exception ex)
            {
                var msg = AutoTranslation.LogPrefix + $"Papago translation failed: {ex.Message}";
                Log.WarningOnce(msg + $", target: {text}", msg.GetHashCode());
                translated = text;
                return false;
            }
        }

        public override bool SupportsCurrentLanguage()
        {
            var lang = LanguageDatabase.activeLanguage;
            if (lang == null)
            {
                return false;
            }

            return _languageMap.ContainsKey(lang.LegacyFolderName);
        }

        private static readonly Dictionary<string, string> _languageMap = new Dictionary<string, string>
        {
            ["Korean"] = "ko",
            ["ChineseSimplified"] = "zh-CN",
            ["ChineseTraditional"] = "zh-TW",
            ["English"] = "en",
            ["Japanese"] = "ja",
            ["French"] = "fr",
            ["German"] = "de",
            ["Spanish"] = "es",
            ["SpanishLatin"] = "es",
            ["Portuguese"] = "pt",
            ["PortugueseBrazilian"] = "pt",
            ["Russian"] = "ru",
            ["Italian"] = "it",
            ["Vietnamese"] = "vi",
            ["Thai"] = "th",
            ["Indonesian"] = "id",
            ["Hindi"] = "hi"
        };

        private static string GetStartLanguage()
        {
            // Papago assumes source is English for RimWorld mods
            // You can override this if needed
            return "en";
        }

        private static string GetTranslateLanguage()
        {
            if (LanguageDatabase.activeLanguage == null)
            {
                return "en";
            }

            var lang = LanguageDatabase.activeLanguage.LegacyFolderName.Split('_').First();

            if (!_languageMap.TryGetValue(lang, out var res))
            {
                Log.Error(AutoTranslation.LogPrefix + $"Papago: Unsupported language: {lang}");
                res = "en";
            }

            return res;
        }
    }
}
