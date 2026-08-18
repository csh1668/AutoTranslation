using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AutoTranslation.Translators;
using AutoTranslation.Utilities;
using Verse;

namespace AutoTranslation.Services
{
    public class TranslationMetadata
    {
        public string TargetLanguage { get; set; }
        public string TranslatorName { get; set; }
        public string TranslatorModel { get; set; }
        public string Date { get; set; }
        public int TranslationCount { get; set; }
    }

    public class GistService
    {
        private const string GistApiUrl = "https://api.github.com/gists";
        private const string GistRawUrl = "https://gist.githubusercontent.com";
        private const string RequestUserAgent = "AutoTranslation_Gist_Uploader";
        
        // Hugslib 방식: 역순으로 저장해 두고 런타임에 뒤집어서 사용 - "ghp_" 접두사를 스캔하는
        // 기계적 크롤러의 토큰 탈취를 피하기 위함. 이 토큰은 Gist 권한만 가짐.
        private readonly string GitHubAuthToken = new string("79zbe0K6z7iFkWOpeRWkn0KwiscnCuGYEkyd_phg".ToCharArray().Reverse().ToArray());
        
        private static readonly Regex GistUrlMatch = new Regex(@"gist\.github\.com/(?:[\w-]+/)?([a-f0-9]{32}|[a-f0-9]{20})", RegexOptions.IgnoreCase);
        private static readonly Regex GistIdMatch = new Regex(@"^([a-f0-9]{32}|[a-f0-9]{20})$", RegexOptions.IgnoreCase);

        public class UploadResult
        {
            public bool Success { get; set; }
            public string GistId { get; set; }
            public string GistUrl { get; set; }
            public string ErrorMessage { get; set; }
        }

        public class DownloadResult
        {
            public bool Success { get; set; }
            public Dictionary<string, string> Translations { get; set; }
            public TranslationMetadata Metadata { get; set; }
            public string ErrorMessage { get; set; }
        }

        public UploadResult UploadTranslation(string translationData, TranslationMetadata metadata)
        {
            try
            {
                var description = $"RimWorld AutoTranslation Cache - {metadata.TargetLanguage} ({metadata.TranslatorName})";
                
                // 메타데이터를 JSON 형태로 생성
                var metadataJson = $"{{\"targetLanguage\":\"{CleanForJSON(metadata.TargetLanguage)}\",\"translatorName\":\"{CleanForJSON(metadata.TranslatorName)}\",\"translatorModel\":\"{CleanForJSON(metadata.TranslatorModel)}\",\"date\":\"{CleanForJSON(metadata.Date)}\",\"translationCount\":{metadata.TranslationCount}}}";
                
                // Gist 페이로드 생성: translations.xml(캐시 파일과 동일 포맷)과 metadata.json
                var payload = $"{{\"description\":\"{CleanForJSON(description)}\",\"public\":true,\"files\":{{\"translations.xml\":{{\"content\":\"{CleanForJSON(translationData)}\"}},\"metadata.json\":{{\"content\":\"{CleanForJSON(metadataJson)}\"}}}}}}";
                
                var headers = new Dictionary<string, string>
                {
                    { "Authorization", $"token {GitHubAuthToken}" },
                    { "User-Agent", RequestUserAgent },
                    { "Accept", "application/vnd.github.v3+json" }
                };

                var response = NetworkHelper.Post(GistApiUrl, payload, headers, "application/json", 3);
                
                if (string.IsNullOrEmpty(response))
                {
                    return new UploadResult
                    {
                        Success = false,
                        ErrorMessage = "Empty response from GitHub API"
                    };
                }

                // 응답에서 Gist ID와 URL 추출
                var gistIdMatch = Regex.Match(response, @"""id"":\s*""([^""]+)""");
                var htmlUrlMatch = Regex.Match(response, @"""html_url"":\s*""([^""]+)""");
                
                if (gistIdMatch.Success)
                {
                    var gistId = gistIdMatch.Groups[1].Value;
                    var gistUrl = htmlUrlMatch.Success ? htmlUrlMatch.Groups[1].Value : $"https://gist.github.com/{gistId}";
                    
                    return new UploadResult
                    {
                        Success = true,
                        GistId = gistId,
                        GistUrl = gistUrl
                    };
                }
                else
                {
                    return new UploadResult
                    {
                        Success = false,
                        ErrorMessage = $"Failed to parse response: {response.Substring(0, Math.Min(200, response.Length))}"
                    };
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{AutoTranslation.LogPrefix}Error uploading to Gist: {ex.Message}");
                return new UploadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        public DownloadResult DownloadTranslation(string input)
        {
            try
            {
                // Gist ID 추출 (URL 또는 직접 ID)
                string gistId = ExtractGistId(input);
                
                if (string.IsNullOrEmpty(gistId))
                {
                    return new DownloadResult
                    {
                        Success = false,
                        ErrorMessage = "Invalid Gist ID or URL format"
                    };
                }

                // Gist API를 통해 메타데이터와 파일 목록 가져오기
                var apiUrl = $"{GistApiUrl}/{gistId}";
                var headers = new Dictionary<string, string>
                {
                    { "User-Agent", RequestUserAgent },
                    { "Accept", "application/vnd.github.v3+json" }
                };

                var gistInfo = NetworkHelper.Get(apiUrl, headers, 3);
                
                if (string.IsNullOrEmpty(gistInfo))
                {
                    return new DownloadResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to fetch Gist information"
                    };
                }

                // translations.xml 파일의 raw_url 추출
                var translationsUrlMatch = Regex.Match(gistInfo, @"""translations\.xml"":\s*\{[^}]*""raw_url"":\s*""([^""]+)""");
                var metadataUrlMatch = Regex.Match(gistInfo, @"""metadata\.json"":\s*\{[^}]*""raw_url"":\s*""([^""]+)""");

                if (!translationsUrlMatch.Success)
                {
                    return new DownloadResult
                    {
                        Success = false,
                        ErrorMessage = "translations.xml file not found in Gist"
                    };
                }

                var translationsUrl = translationsUrlMatch.Groups[1].Value;
                var translationsData = NetworkHelper.Get(translationsUrl, headers, 3);
                
                if (string.IsNullOrEmpty(translationsData))
                {
                    return new DownloadResult
                    {
                        Success = false,
                        ErrorMessage = "Failed to download translations data"
                    };
                }

                // 메타데이터 다운로드 (선택적)
                TranslationMetadata metadata = null;
                if (metadataUrlMatch.Success)
                {
                    try
                    {
                        var metadataUrl = metadataUrlMatch.Groups[1].Value;
                        var metadataJson = NetworkHelper.Get(metadataUrl, headers, 3);
                        
                        if (!string.IsNullOrEmpty(metadataJson))
                        {
                            metadata = ParseMetadata(metadataJson);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"{AutoTranslation.LogPrefix}Failed to parse metadata: {ex.Message}");
                    }
                }

                // translations.xml 파싱 (온디스크 캐시와 동일 포맷)
                var translations = TranslationCacheManager.ParseCacheXml(translationsData);
                
                if (translations == null || translations.Count == 0)
                {
                    return new DownloadResult
                    {
                        Success = false,
                        ErrorMessage = "No translations found in Gist"
                    };
                }

                // 메타데이터가 없으면 기본값 설정
                if (metadata == null)
                {
                    metadata = new TranslationMetadata
                    {
                        TargetLanguage = "Unknown",
                        TranslatorName = "Unknown",
                        TranslatorModel = "Unknown",
                        Date = "Unknown",
                        TranslationCount = translations.Count
                    };
                }
                else
                {
                    metadata.TranslationCount = translations.Count;
                }

                return new DownloadResult
                {
                    Success = true,
                    Translations = translations,
                    Metadata = metadata
                };
            }
            catch (Exception ex)
            {
                Log.Error($"{AutoTranslation.LogPrefix}Error downloading from Gist: {ex.Message}");
                return new DownloadResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private string ExtractGistId(string input)
        {
            if (string.IsNullOrEmpty(input))
                return null;

            input = input.Trim();

            // URL 형식인지 확인
            var urlMatch = GistUrlMatch.Match(input);
            if (urlMatch.Success)
            {
                return urlMatch.Groups[1].Value;
            }

            // 직접 ID 형식인지 확인
            var idMatch = GistIdMatch.Match(input);
            if (idMatch.Success)
            {
                return idMatch.Groups[1].Value;
            }

            return null;
        }


        private TranslationMetadata ParseMetadata(string jsonData)
        {
            try
            {
                var metadata = new TranslationMetadata();
                
                var targetLangMatch = Regex.Match(jsonData, @"""targetLanguage""\s*:\s*""([^""]*)""");
                var translatorNameMatch = Regex.Match(jsonData, @"""translatorName""\s*:\s*""([^""]*)""");
                var translatorModelMatch = Regex.Match(jsonData, @"""translatorModel""\s*:\s*""([^""]*)""");
                var dateMatch = Regex.Match(jsonData, @"""date""\s*:\s*""([^""]*)""");
                var countMatch = Regex.Match(jsonData, @"""translationCount""\s*:\s*(\d+)");
                
                if (targetLangMatch.Success)
                    metadata.TargetLanguage = UnescapeJson(targetLangMatch.Groups[1].Value);
                if (translatorNameMatch.Success)
                    metadata.TranslatorName = UnescapeJson(translatorNameMatch.Groups[1].Value);
                if (translatorModelMatch.Success)
                    metadata.TranslatorModel = UnescapeJson(translatorModelMatch.Groups[1].Value);
                if (dateMatch.Success)
                    metadata.Date = UnescapeJson(dateMatch.Groups[1].Value);
                if (countMatch.Success)
                    metadata.TranslationCount = int.Parse(countMatch.Groups[1].Value);
                
                return metadata;
            }
            catch (Exception ex)
            {
                Log.Error($"{AutoTranslation.LogPrefix}Error parsing metadata JSON: {ex.Message}");
                return null;
            }
        }

        private string UnescapeJson(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;
            
            return str
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t");
        }

        private static string CleanForJSON(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            
            int length = s.Length;
            StringBuilder stringBuilder = new StringBuilder(length + 4);
            for (int index = 0; index < length; index++)
            {
                char ch = s[index];
                switch (ch)
                {
                    case '\b':
                        stringBuilder.Append("\\b");
                        break;
                    case '\t':
                        stringBuilder.Append("\\t");
                        break;
                    case '\n':
                        stringBuilder.Append("\\n");
                        break;
                    case '\f':
                        stringBuilder.Append("\\f");
                        break;
                    case '\r':
                        stringBuilder.Append("\\r");
                        break;
                    case '"':
                    case '\\':
                        stringBuilder.Append('\\');
                        stringBuilder.Append(ch);
                        break;
                    case '/':
                        stringBuilder.Append('\\');
                        stringBuilder.Append(ch);
                        break;
                    default:
                        if (ch < ' ')
                        {
                            string str = "000" + ((int)ch).ToString("X4");
                            stringBuilder.Append("\\u" + str.Substring(str.Length - 4));
                            break;
                        }
                        stringBuilder.Append(ch);
                        break;
                }
            }
            return stringBuilder.ToString();
        }
    }
}

