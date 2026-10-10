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
        private const string urlFormat = "https://{0}/translate_a/single?client=at&sl={1}&tl={2}&dt=t&ie=UTF-8&oe=UTF-8&q={3}";
        private const string DirectHost = "translate.google.com";
        private const string ChinaProxyHost = "google-translate-proxy.tantu.com";

        private static readonly Dictionary<string, string> requestHeaders = new Dictionary<string, string>
        {
            ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36"
        };

        private const int QueueBatchTokens = 750;
        private const int MaxBatchQueryLength = 6000;

        private const int RequestsPerMinute = 60;
        private static readonly RateLimiter rateLimiter = new RateLimiter(RequestsPerMinute);

        private static readonly TimeSpan BanCooldown = TimeSpan.FromMinutes(5);
        private const int MaxBanWaits = 3;

        public override string Name  => "Google";

        // Not cached: the user can change the target language override at runtime
        public override string TranslateLanguage => GetTranslateLanguage();
        public override bool RequiresKey => false;

        public override void Prepare()
        {
            // No connectivity preflight
            Ready = true;
        }

        public override bool SupportsBatchTranslation => true;
        public override int BatchSizeTokens => QueueBatchTokens;

        public override bool TryTranslate(string text, out string translated)
        {
            try
            {
                // Protect placeholders
                var (protectedText, placeholders) = text.ProtectPlaceholders();

                var targetLang = TranslateLanguage;
                var t = ParseResult(RequestWithCooldown(BuildUrl(protectedText, targetLang)), out var detectedLang);

                // Restore placeholders
                var (restoredText, allRestored) = t.RestorePlaceholders(placeholders);

                translated = detectedLang == targetLang ? text : restoredText;

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

        private sealed class BatchItem
        {
            public string Original;
            public Dictionary<string, string> Placeholders;
            public string[] Lines;
            public List<int> SentLines;
            public int EscapedLength;
        }

        public override bool TryTranslateBatch(List<string> texts, out List<string> translated)
        {
            translated = new List<string>(texts ?? new List<string>());
            if (translated.Count == 0) return true;

            try
            {
                var targetLang = TranslateLanguage;
                var allSuccess = true;
                var chunk = new List<int>();
                var chunkLength = 0;
                var items = texts.Select(PrepareBatchItem).ToList();

                for (int i = 0; i < items.Count; i++)
                {
                    if (chunk.Count > 0 && chunkLength + items[i].EscapedLength > MaxBatchQueryLength)
                    {
                        allSuccess &= TranslateChunk(items, chunk, targetLang, translated);
                        chunk.Clear();
                        chunkLength = 0;
                    }
                    chunk.Add(i);
                    chunkLength += items[i].EscapedLength;
                }
                if (chunk.Count > 0) allSuccess &= TranslateChunk(items, chunk, targetLang, translated);

                return allSuccess;
            }
            catch (Exception e)
            {
                var msg = AutoTranslation.LogPrefix + $"{Name}, batch translate failed. reason: {e.GetType()}|{e.Message}";
                Log.WarningOnce(msg, msg.GetHashCode());
                translated = new List<string>(texts);
                return false;
            }
        }

        private static BatchItem PrepareBatchItem(string text)
        {
            var (protectedText, placeholders) = text.ProtectPlaceholders();
            var item = new BatchItem
            {
                Original = text,
                Placeholders = placeholders,
                Lines = (protectedText ?? string.Empty).Split('\n'),
                SentLines = new List<int>()
            };
            for (int i = 0; i < item.Lines.Length; i++)
            {
                var line = item.Lines[i].Trim();
                if (line.Length == 0) continue; // blank lines are kept locally, never sent
                item.SentLines.Add(i);
                item.EscapedLength += UnityWebRequest.EscapeURL(line).Length + 3; // + "%0A"
            }
            return item;
        }

        private bool TranslateChunk(List<BatchItem> items, List<int> chunk, string targetLang, List<string> results)
        {
            if (chunk.Count == 1) return TranslateSingle(items[chunk[0]], chunk[0], results);

            var sendLines = new List<string>();
            foreach (var idx in chunk)
                foreach (var li in items[idx].SentLines)
                    sendLines.Add(items[idx].Lines[li].Trim());

            if (sendLines.Count == 0)
            {
                foreach (var idx in chunk) results[idx] = items[idx].Original;
                return true;
            }

            var response = ParseResult(RequestWithCooldown(BuildUrl(string.Join("\n", sendLines), targetLang)), out var detectedLang);
            var outLines = response.Split('\n');
            var outCount = outLines.Length;
            if (outCount == sendLines.Count + 1 && outLines[outCount - 1].Trim().Length == 0) outCount--;

            if (outCount != sendLines.Count || detectedLang == targetLang)
            {
                Log.Warning(AutoTranslation.LogPrefix + $"{Name}: batch response did not match the request ({outCount}/{sendLines.Count} lines, detected '{detectedLang}'). Translating {chunk.Count} texts one by one.");
                var ok = true;
                foreach (var idx in chunk) ok &= TranslateSingle(items[idx], idx, results);
                return ok;
            }

            var allSuccess = true;
            var cursor = 0;
            foreach (var idx in chunk)
            {
                var item = items[idx];
                var lines = (string[])item.Lines.Clone();
                foreach (var li in item.SentLines)
                {
                    var original = lines[li];
                    var leading = original.Substring(0, original.Length - original.TrimStart().Length);
                    var trailing = original.Substring(original.TrimEnd().Length);
                    lines[li] = leading + outLines[cursor++].Trim() + trailing;
                }

                var (restoredText, allRestored) = string.Join("\n", lines).RestorePlaceholders(item.Placeholders);
                if (allRestored)
                    results[idx] = restoredText;
                else
                    allSuccess &= TranslateSingle(item, idx, results);
            }
            return allSuccess;
        }

        private bool TranslateSingle(BatchItem item, int idx, List<string> results)
        {
            var ok = TryTranslate(item.Original, out var single);
            results[idx] = single;
            return ok;
        }

        private string BuildUrl(string text, string targetLang)
        {
            var host = targetLang == "zh-CN" ? ChinaProxyHost : DirectHost;
            return string.Format(urlFormat, host, StartLanguage, targetLang, UnityWebRequest.EscapeURL(text));
        }

        private string RequestWithCooldown(string url)
        {
            for (int bans = 0; ; bans++)
            {
                rateLimiter.Wait();
                try
                {
                    return NetworkHelper.Get(url, requestHeaders, retryOnRateLimit: false);
                }
                catch (WebException e) when (NetworkHelper.IsRateLimited(e) && bans < MaxBanWaits)
                {
                    if (rateLimiter.PauseFor(BanCooldown))
                    {
                        Log.Warning(AutoTranslation.LogPrefix + $"{Name}: rate limited by Google (429). Pausing Google requests for {BanCooldown.TotalMinutes} minutes.");
                    }
                }
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
            var sb = new StringBuilder(text.Length);
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
