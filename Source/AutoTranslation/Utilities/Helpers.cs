using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEngine;
using Verse;

namespace AutoTranslation.Utilities
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

        // (?:\\.|[^"\\])* is the proper JSON string body: an escape pair or any non-quote,
        // non-backslash char - the old [^"] class could swallow the backslash of a trailing \\
        private const string JsonStringBodyPattern = "((?:\\\\.|[^\"\\\\])*)";

        public static string GetStringValueFromJson(this string json, string key)
        {
            var pattern = $"\"{key}\"\\s*:\\s*\"{JsonStringBodyPattern}\"";
            var match = Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value.UnescapeJsonString() : null;
        }

        public static List<string> GetStringValuesFromJson(this string json, string key)
        {
            var pattern = $"\"{key}\"\\s*:\\s*\"{JsonStringBodyPattern}\"";
            var matches = Regex.Matches(json, pattern);
            return matches.Cast<Match>().Select(match => match.Groups[1].Value.UnescapeJsonString()).ToList();
        }

        public static long? GetLongValueFromJson(this string json, string key)
        {
            var match = Regex.Match(json, $"\"{key}\"\\s*:\\s*(\\d+)");
            return match.Success && long.TryParse(match.Groups[1].Value, out var v) ? v : (long?)null;
        }

        /// <summary>
        /// Decodes JSON string escapes (\n, \t, \", \\, \uXXXX, ...). Unknown escapes are kept as-is.
        /// </summary>
        public static string UnescapeJsonString(this string input)
        {
            if (string.IsNullOrEmpty(input) || input.IndexOf('\\') < 0) return input;

            var sb = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (c != '\\' || i == input.Length - 1)
                {
                    sb.Append(c);
                    continue;
                }

                var next = input[++i];
                switch (next)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 < input.Length &&
                            ushort.TryParse(input.Substring(i + 1, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                        {
                            sb.Append((char)code);
                            i += 4;
                        }
                        else
                        {
                            sb.Append('\\').Append(next);
                        }
                        break;
                    default:
                        sb.Append('\\').Append(next);
                        break;
                }
            }
            return sb.ToString();
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
        /// <summary>
        /// Strips custom-language suffixes from a language folder name:
        /// "Russian-SK" / "Korean_..." both resolve to the base language name.
        /// </summary>
        public static string NormalizeLanguageFolder(this string folder)
        {
            if (string.IsNullOrEmpty(folder)) return folder;
            return folder.Split('_')[0].Split('-')[0].Trim();
        }

        /// <summary>
        /// The language identity translations should target: the user's manual override
        /// if set, otherwise the active language's folder name.
        /// </summary>
        public static string EffectiveLanguageFolder()
        {
            var overrideLang = global::AutoTranslation.Settings.TargetLanguageOverride;
            if (!string.IsNullOrWhiteSpace(overrideLang)) return overrideLang.Trim();
            return LanguageDatabase.activeLanguage?.LegacyFolderName;
        }

        public static bool HasLanguageOverride()
        {
            return !string.IsNullOrWhiteSpace(global::AutoTranslation.Settings.TargetLanguageOverride);
        }

        /// <summary>
        /// Resolves the target language code for a translator's language map.
        /// Tries the normalized name, then the raw name; a manual override that matches
        /// neither falls back to the shared name-to-code table (so "Latvian" works even
        /// on engines whose own map lacks it), and finally passes through verbatim so
        /// raw codes like "lv" keep working. Returns null when nothing resolves.
        /// </summary>
        public static string ResolveTargetLanguage(Dictionary<string, string> languageMap)
        {
            var folder = EffectiveLanguageFolder();
            if (string.IsNullOrEmpty(folder)) return null;

            if (languageMap.TryGetValue(folder.NormalizeLanguageFolder(), out var mapped)) return mapped;
            if (languageMap.TryGetValue(folder, out mapped)) return mapped;

            if (HasLanguageOverride())
            {
                if (KnownLanguageCodes.TryGetValue(folder.NormalizeLanguageFolder(), out var code)) return code;
                return folder;
            }

            return null;
        }

        /// <summary>
        /// Language names selectable in the target-language dropdown, sorted.
        /// </summary>
        public static List<string> KnownLanguageNames => KnownLanguageCodes.Keys.OrderBy(x => x).ToList();

        // Shared name -> ISO code fallback used when a translator's own map lacks the language.
        // Names follow RimWorld's language folder naming so they also hit the engine maps directly.
        private static readonly Dictionary<string, string> KnownLanguageCodes = new Dictionary<string, string>
        {
            ["Albanian"] = "sq",
            ["Arabic"] = "ar",
            ["Azerbaijani"] = "az",
            ["Basque"] = "eu",
            ["Belarusian"] = "be",
            ["Bulgarian"] = "bg",
            ["Catalan"] = "ca",
            ["ChineseSimplified"] = "zh-CN",
            ["ChineseTraditional"] = "zh-TW",
            ["Croatian"] = "hr",
            ["Czech"] = "cs",
            ["Danish"] = "da",
            ["Dutch"] = "nl",
            ["English"] = "en",
            ["Esperanto"] = "eo",
            ["Estonian"] = "et",
            ["Finnish"] = "fi",
            ["French"] = "fr",
            ["Georgian"] = "ka",
            ["German"] = "de",
            ["Greek"] = "el",
            ["Hebrew"] = "he",
            ["Hindi"] = "hi",
            ["Hungarian"] = "hu",
            ["Indonesian"] = "id",
            ["Italian"] = "it",
            ["Japanese"] = "ja",
            ["Kazakh"] = "kk",
            ["Korean"] = "ko",
            ["Latvian"] = "lv",
            ["Lithuanian"] = "lt",
            ["Macedonian"] = "mk",
            ["Malay"] = "ms",
            ["Mongolian"] = "mn",
            ["Norwegian"] = "no",
            ["Persian"] = "fa",
            ["Polish"] = "pl",
            ["Portuguese"] = "pt",
            ["PortugueseBrazilian"] = "pt-BR",
            ["Romanian"] = "ro",
            ["Russian"] = "ru",
            ["Serbian"] = "sr",
            ["Slovak"] = "sk",
            ["Slovenian"] = "sl",
            ["Spanish"] = "es",
            ["SpanishLatin"] = "es",
            ["Swedish"] = "sv",
            ["Thai"] = "th",
            ["Turkish"] = "tr",
            ["Ukrainian"] = "uk",
            ["Vietnamese"] = "vi"
        };

        // Persistent per-field text buffers for decimal input. RimWorld's TextFieldNumeric
        // re-parses and re-formats every frame, which eats an in-progress "." (typing "0.5"
        // becomes impossible). Keeping the raw string across frames and parsing on the side
        // lets intermediate states like "0." live until the user finishes typing.
        private static readonly Dictionary<string, string> _decimalBuffers = new Dictionary<string, string>();

        public static float DecimalTextField(Rect rect, string bufferKey, float value)
        {
            if (!_decimalBuffers.TryGetValue(bufferKey, out var buffer))
            {
                buffer = value == 0f ? "0" : value.ToString("0.######", CultureInfo.InvariantCulture);
            }

            var typed = Widgets.TextField(rect, buffer);
            // Allow only digits and one decimal separator; ',' is accepted and treated as '.'
            typed = Regex.Replace(typed ?? string.Empty, @"[^0-9.,]", "");
            _decimalBuffers[bufferKey] = typed;

            var normalized = typed.Replace(',', '.');
            if (float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0f)
            {
                return parsed;
            }
            // Empty or partial input like "." keeps the last committed value
            return string.IsNullOrEmpty(normalized) || normalized == "." ? 0f : value;
        }

        public static void ClearDecimalBuffer(string bufferKey)
        {
            _decimalBuffers.Remove(bufferKey);
        }

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

