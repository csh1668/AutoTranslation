using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using AutoTranslation.Translators;

namespace AutoTranslation
{
    internal static class Helpers
    {
        public static string FitFormat(this string str, int cnt)
        {
            //str = str.Replace("\"{", "{").Replace("}\"", "}")
            //    .Replace("「{", "{").Replace("}」", "}");
            var pattern = @"\{\d+\}";
            var matches = Regex.Matches(str, pattern);
            var curCnt = matches.Count;
            if (curCnt == cnt) return str;
            if (curCnt > cnt)
            {
                return Regex.Replace(str, pattern, match => int.Parse(match.Groups[1].Value) >= cnt ? "" : match.Value);
            }
            var sb = new StringBuilder(str);
            if (curCnt < cnt)
            {
                while (curCnt < cnt) sb.Append($"|{{{curCnt++}}}|");
            }
            return sb.ToString();
        }

        public static (string, List<string>) ToFormatString(this string str)
        {
            string capture = @"[\[\{](.*?)[\]\}]";
            var placeholders = new List<string>();
            var formatString = Regex.Replace(str, capture, match =>
            {
                var placeholder = match.Groups[1].Value;
                if (match.Value.StartsWith("["))
                {
                    placeholders.Add($"[{placeholder}]");
                }
                else if (match.Value.StartsWith("{"))
                {
                    placeholders.Add($"{{{placeholder}}}");
                }
                return $"{{{placeholders.Count - 1}}}";
            });
            return (formatString, placeholders);
        }

        public static string[] Tokenize(this string str) =>
            str.Split(new[] { ".\n", ". " }, StringSplitOptions.RemoveEmptyEntries);

        public static string GetStringValueFromJson(this string json, string key)
        {
            var pattern = $"\"{key}\"\\s*:\\s*\"((?:\\\\\"|[^\"])*)\"";
            var match = Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value.Replace("\\\"", "\"") : null;
        }

        public static List<string> GetStringValuesFromJson(this string json, string key)
        {
            var pattern = $"\"{key}\"\\s*:\\s*\"((?:\\\\\"|[^\"])*)\"";
            var matches = Regex.Matches(json, pattern);
            return matches.Cast<Match>().Select(match => match.Groups[1].Value.Replace("\\\"", "\"")).ToList();
        }

        #region XmlHelpers

        public static XmlElement Append(this XmlElement parent, Action<XmlElement> work)
        {
            work(parent);
            return parent;
        }

        public static XmlElement AppendElement(this XmlNode parent, string name, string innerText = null)
        {
            var child = (XmlElement)parent.AppendChild(
                (parent.NodeType == XmlNodeType.Document ? (XmlDocument)parent : parent.OwnerDocument)
                .CreateElement(name)) ?? throw new NullReferenceException();
            if (innerText != null)
            {
                child.InnerText = innerText;
            }

            return child;
        }
        public static XmlElement AppendElement(this XmlElement parent, string name, string innerText = null)
        {
            var child = (XmlElement)parent.AppendChild(parent.OwnerDocument.CreateElement(name)) ??
                        throw new NullReferenceException();

            if (innerText != null)
            {
                child.InnerText = innerText;
            }
            return child;
        }

        public static XmlElement AppendElement(this XmlNode parent, string name, Action<XmlElement> work)
        {
            var child = parent.AppendElement(name);
            work(child);
            return child;
        }

        public static XmlAttribute AppendAttribute(this XmlNode parent, string name, string value)
        {
            if (parent is XmlElement e)
                return e.AppendAttribute(name, value);
            else
                return null;
        }
        public static XmlAttribute AppendAttribute(this XmlElement parent, string name, string value)
        {
            var attr = parent.Attributes.Append(parent.OwnerDocument.CreateAttribute(name));
            if (value != null)
            {
                attr.Value = value;
            }
            return attr;
        }

        #endregion

        /// <summary>
        /// Safely combines URL segments, ensuring proper forward slashes.
        /// </summary>
        public static string CombineUrl(params string[] segments)
        {
            if (segments == null || segments.Length == 0)
                return string.Empty;
            
            var sb = new StringBuilder();
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (string.IsNullOrEmpty(segment))
                    continue;
                
                // First segment: keep as-is (may contain protocol like https://)
                if (i == 0)
                {
                    sb.Append(segment.TrimEnd('/'));
                }
                else
                {
                    // Add separator if needed
                    if (sb.Length > 0 && sb[sb.Length - 1] != '/')
                        sb.Append('/');
                    
                    // Trim leading and trailing slashes from middle segments
                    sb.Append(segment.Trim('/'));
                }
            }
            
            return sb.ToString();
        }

        public static string EscapeJsonString(this string input)
        {
            var sb = new StringBuilder(input.Length);
            foreach (var c in input)
            {
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Protects placeholders and special tags from being modified by AI translation.
        /// Replaces {0}, {PlayerName}, [itemLabel], <color=#fff>, etc. with safe tokens like __PH0__, __PH1__, etc.
        /// </summary>
        public static (string protectedText, Dictionary<string, string> placeholders) ProtectPlaceholders(this string text)
        {
            if (string.IsNullOrEmpty(text)) return (text, new Dictionary<string, string>());

            var placeholders = new Dictionary<string, string>();
            var counter = 0;
            var result = text;

            // Pattern to match: {anything}, [anything], <tag attributes>, or \n, \t escape sequences
            // Order matters: longer patterns first to avoid partial matches
            var patterns = new[]
            {
                @"<[^>]+>",           // HTML/XML tags: <color=#fff>, <b>, </color>, etc.
                @"\{[^\}]+\}",        // Curly braces: {0}, {PlayerName}, etc.
                @"\[[^\]]+\]",        // Square brackets: [itemLabel], etc.
                @"\\[nrt]"            // Escape sequences: \n, \r, \t
            };

            foreach (var pattern in patterns)
            {
                result = Regex.Replace(result, pattern, match =>
                {
                    var token = $"__PH{counter}__";
                    placeholders[token] = match.Value;
                    counter++;
                    return token;
                });
            }

            return (result, placeholders);
        }

        /// <summary>
        /// Restores placeholders that were protected by ProtectPlaceholders.
        /// Returns the restored text and a boolean indicating if all placeholders were found.
        /// </summary>
        public static (string restoredText, bool allRestored) RestorePlaceholders(this string translatedText, Dictionary<string, string> placeholders)
        {
            if (string.IsNullOrEmpty(translatedText) || placeholders == null || placeholders.Count == 0)
                return (translatedText, true);

            var result = translatedText;
            var allFound = true;

            foreach (var kvp in placeholders)
            {
                if (result.Contains(kvp.Key))
                {
                    result = result.Replace(kvp.Key, kvp.Value);
                }
                else
                {
                    // Token not found in translated text - AI might have removed it
                    allFound = false;
                }
            }

            // Check if any tokens are still remaining (shouldn't happen if everything worked)
            if (result.Contains("__PH") && Regex.IsMatch(result, @"__PH\d+__"))
            {
                allFound = false;
            }

            return (result, allFound);
        }

        /// <summary>
        /// Validates that the translated text has the same placeholder count as the original.
        /// </summary>
        public static bool ValidatePlaceholderCount(this string original, string translated)
        {
            var originalCurly = Regex.Matches(original, @"\{[^\}]*\}").Count;
            var translatedCurly = Regex.Matches(translated, @"\{[^\}]*\}").Count;
            
            var originalSquare = Regex.Matches(original, @"\[[^\]]*\]").Count;
            var translatedSquare = Regex.Matches(translated, @"\[[^\]]*\]").Count;
            
            var originalTags = Regex.Matches(original, @"<[^>]+>").Count;
            var translatedTags = Regex.Matches(translated, @"<[^>]+>").Count;

            return originalCurly == translatedCurly && 
                   originalSquare == translatedSquare && 
                   originalTags == translatedTags;
        }
    }
}
