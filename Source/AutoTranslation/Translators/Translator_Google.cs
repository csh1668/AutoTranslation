using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine.Networking;
using Verse;
using AutoTranslation;
using AutoTranslation.Utilities;

namespace AutoTranslation.Translators
{
    public class Translator_Google : Translator_BaseTraditional
    {
        private const string testUrl = "https://translate.google.com";
        private const string urlFormat = "https://translate.google.com/translate_a/single?client=at&sl={0}&tl={1}&dt=t&ie=UTF-8&oe=UTF-8&q={2}";
        private static readonly StringBuilder sb = new StringBuilder(1024);

        public override string Name  => "Google";

        // Not cached: the user can change the target language override at runtime
        public override string TranslateLanguage => GetTranslateLanguage();
        public override bool RequiresKey => false;

        public override void Prepare()
        {
            // No connectivity preflight: it ran synchronously during game load and could
            // block for minutes when the endpoint is unreachable (e.g. no VPN in China).
            // Reachability is validated by real translation requests + the circuit breaker.
            Ready = true;
        }

        public override bool TryTranslate(string text, out string translated)
        {
            try
            {
                // Protect placeholders
                var (protectedText, placeholders) = text.ProtectPlaceholders();

                var url = string.Format(urlFormat, StartLanguage, TranslateLanguage, UnityWebRequest.EscapeURL(protectedText));
                var t = ParseResult(NetworkHelper.Get(url), out var detectedLang);

                // Restore placeholders
                var (restoredText, allRestored) = t.RestorePlaceholders(placeholders);

                translated = detectedLang == TranslateLanguage ? text : restoredText;

                if (!allRestored)
                {
                    Log.Warning(AutoTranslation.LogPrefix + $"{Name}: Some placeholders were not properly restored. Using original text.");
                    translated = text;
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                var msg = AutoTranslation.LogPrefix + $"{Name}, translate failed. reason: {e.GetType()}|{e.Message}";
                Log.WarningOnce(msg + $", target: {text}", msg.GetHashCode());
                translated = text;
                return false;
            }
        }

        public override bool SupportsCurrentLanguage()
        {
            if (Helpers.HasLanguageOverride()) return true;

            var lang = LanguageDatabase.activeLanguage;
            if (lang == null)
            {
                Log.Warning(AutoTranslation.LogPrefix + "activeLanguage was null");
                return false;
            }

            return TranslateLanguageGetter.ContainsKey(lang.LegacyFolderName.NormalizeLanguageFolder());
        }


        internal static string ParseResult(string text, out string detectedLang)
        {
            sb.Clear();
            detectedLang = string.Empty;

            int depth = 0;
            int i = 0;
            int stringIndexInSegment = 0;
            bool inSegments = true; // still inside the first (segments) array
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '"')
                {
                    var str = ReadJsonString(text, ref i);
                    if (inSegments && depth == 3 && stringIndexInSegment == 0)
                    {
                        sb.Append(str);
                    }
                    else if (!inSegments && depth == 1 && detectedLang.Length == 0)
                    {
                        detectedLang = str;
                    }
                    stringIndexInSegment++;
                    continue;
                }

                if (c == '[')
                {
                    depth++;
                    if (depth == 3) stringIndexInSegment = 0;
                }
                else if (c == ']')
                {
                    depth--;
                    if (depth == 1 && inSegments) inSegments = false; // closed the segments array
                }
                i++;
            }

            return sb.ToString();
        }

        private static string ReadJsonString(string text, ref int i)
        {
            var result = new StringBuilder();
            i++; // skip opening quote
            while (i < text.Length)
            {
                var c = text[i];
                if (c == '"') { i++; break; }
                if (c == '\\' && i + 1 < text.Length)
                {
                    i++;
                    var e = text[i];
                    switch (e)
                    {
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'u':
                            if (i + 4 < text.Length && int.TryParse(text.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var code))
                            {
                                result.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: result.Append(e); break; // \" \\ \/
                    }
                    i++;
                    continue;
                }
                result.Append(c);
                i++;
            }
            return result.ToString();
        }

        private static readonly Dictionary<string, string> TranslateLanguageGetter = new Dictionary<string, string>
        {
            ["Korean"] = "ko",
            ["Catalan"] = "ca",
            ["ChineseSimplified"] = "zh-CN",
            ["ChineseTraditional"] = "zh-TW",
            ["Czech"] = "cs",
            ["Danish"] = "da",
            ["Dutch"] = "nl",
            ["Estonian"] = "et",
            ["Finnish"] = "fi",
            ["French"] = "fr",
            ["German"] = "de",
            ["Greek"] = "el",
            ["Hungarian"] = "hu",
            ["Italian"] = "it",
            ["Japanese"] = "ja",
            ["Norwegian"] = "no",
            ["Polish"] = "pl",
            ["Portuguese"] = "pt-PT",
            ["PortugueseBrazilian"] = "pt",
            ["Romanian"] = "ro",
            ["Russian"] = "ru",
            ["Slovak"] = "sk",
            ["SpanishLatin"] = "es",
            ["Spanish"] = "es",
            ["Swedish"] = "sv",
            ["Turkish"] = "tr",
            ["Ukrainian"] = "uk",
            ["English"] = "en",
            ["Vietnamese"] = "vi",
            ["Thai"] = "th"
        };

        private static string GetTranslateLanguage()
        {
            var res = Helpers.ResolveTargetLanguage(TranslateLanguageGetter);
            if (res == null)
            {
                Log.Error(AutoTranslation.LogPrefix + $"Unsupported language: {LanguageDatabase.activeLanguage?.LegacyFolderName ?? "(null)"}");
                res = "en";
            }
            return res;
        }
    }
}
