using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace AutoTranslation.Services
{
    public static class TranslationSerializer
    {
        /// <summary>
        /// 번역 캐시를 JSON 형식의 문자열로 직렬화합니다.
        /// </summary>
        public static string SerializeToJson(Dictionary<string, string> translations)
        {
            if (translations == null || translations.Count == 0)
            {
                return "[]";
            }

            var sb = new StringBuilder();
            sb.Append("[");

            bool first = true;
            foreach (var kvp in translations.OrderBy(x => x.Key))
            {
                if (!first)
                {
                    sb.Append(",");
                }
                first = false;

                sb.Append("{");
                sb.Append($"\"key\":\"{EscapeJson(kvp.Key)}\",");
                sb.Append($"\"value\":\"{EscapeJson(kvp.Value)}\"");
                sb.Append("}");
            }

            sb.Append("]");
            return sb.ToString();
        }

        /// <summary>
        /// JSON 문자열을 번역 딕셔너리로 역직렬화합니다.
        /// </summary>
        public static Dictionary<string, string> DeserializeFromJson(string json)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(json))
            {
                return result;
            }

            try
            {
                // 간단한 JSON 파싱 (배열 형식)
                // 형식: [{"key":"...","value":"..."},...]
                var entries = System.Text.RegularExpressions.Regex.Matches(
                    json,
                    @"""key""\s*:\s*""([^""]*)""\s*,\s*""value""\s*:\s*""([^""]*)""");

                foreach (System.Text.RegularExpressions.Match match in entries)
                {
                    var key = UnescapeJson(match.Groups[1].Value);
                    var value = UnescapeJson(match.Groups[2].Value);
                    result[key] = value;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{AutoTranslation.LogPrefix}Error deserializing JSON: {ex.Message}");
            }

            return result;
        }

        private static string EscapeJson(string s)
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

        private static string UnescapeJson(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            return str
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\")
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\b", "\b")
                .Replace("\\f", "\f");
        }
    }
}


