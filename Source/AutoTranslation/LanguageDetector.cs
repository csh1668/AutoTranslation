using System;
using System.Linq;
using System.Text.RegularExpressions;
using Verse;

namespace AutoTranslation
{
    /// <summary>
    /// 텍스트의 언어를 감지하는 유틸리티 클래스
    /// </summary>
    public static class LanguageDetector
    {
        // 유니코드 범위 기반 언어 감지
        private static readonly Regex KoreanRegex = new Regex(@"[\uAC00-\uD7A3\u1100-\u11FF\u3130-\u318F]", RegexOptions.Compiled);
        private static readonly Regex JapaneseRegex = new Regex(@"[\u3040-\u309F\u30A0-\u30FF\u31F0-\u31FF]", RegexOptions.Compiled);
        private static readonly Regex ChineseRegex = new Regex(@"[\u4E00-\u9FFF\u3400-\u4DBF]", RegexOptions.Compiled);
        private static readonly Regex CyrillicRegex = new Regex(@"[\u0400-\u04FF]", RegexOptions.Compiled); // Russian, Ukrainian
        private static readonly Regex ArabicRegex = new Regex(@"[\u0600-\u06FF\u0750-\u077F]", RegexOptions.Compiled);
        private static readonly Regex ThaiRegex = new Regex(@"[\u0E00-\u0E7F]", RegexOptions.Compiled);
        private static readonly Regex HebrewRegex = new Regex(@"[\u0590-\u05FF]", RegexOptions.Compiled);
        private static readonly Regex GreekRegex = new Regex(@"[\u0370-\u03FF\u1F00-\u1FFF]", RegexOptions.Compiled);
        private static readonly Regex VietnameseRegex = new Regex(@"[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềấếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        
        // 영어 및 라틴 문자 기반 언어 (기본)
        private static readonly Regex LatinRegex = new Regex(@"[a-zA-ZÀ-ÿ]", RegexOptions.Compiled);

        public enum DetectedLanguage
        {
            Unknown,
            English,
            Korean,
            Japanese,
            ChineseSimplified,
            ChineseTraditional,
            Russian,
            Ukrainian,
            Arabic,
            Thai,
            Hebrew,
            Greek,
            Vietnamese,
            Turkish,
            Polish,
            Czech,
            German,
            French,
            Italian,
            Spanish,
            Portuguese,
            Dutch,
            Swedish,
            Norwegian,
            Danish,
            Finnish,
            Romanian,
            Hungarian,
            Catalan,
            Slovak,
            Estonian,
            Mixed,
            Latin  // Generic Latin-based language
        }

        /// <summary>
        /// 텍스트의 주요 언어를 감지합니다.
        /// </summary>
        /// <param name="text">분석할 텍스트</param>
        /// <returns>감지된 언어</returns>
        public static DetectedLanguage Detect(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return DetectedLanguage.Unknown;

            // 공백 제거하고 실제 문자만 카운트
            var trimmedText = Regex.Replace(text, @"\s+", "");
            if (trimmedText.Length < 2)
                return DetectedLanguage.Unknown;

            int totalChars = trimmedText.Length;
            
            // 각 언어별 문자 수 카운트 (우선순위: 고유 문자 체계가 있는 언어)
            int koreanCount = KoreanRegex.Matches(text).Count;
            int japaneseCount = JapaneseRegex.Matches(text).Count;
            int chineseCount = ChineseRegex.Matches(text).Count;
            int cyrillicCount = CyrillicRegex.Matches(text).Count; // Russian/Ukrainian
            int arabicCount = ArabicRegex.Matches(text).Count;
            int thaiCount = ThaiRegex.Matches(text).Count;
            int hebrewCount = HebrewRegex.Matches(text).Count;
            int greekCount = GreekRegex.Matches(text).Count;
            int vietnameseCount = VietnameseRegex.Matches(text).Count;
            int latinCount = LatinRegex.Matches(text).Count;

            // 비율 계산 (최소 20% 이상이면 해당 언어로 판단)
            const double threshold = 0.2;
            
            // 각 언어의 비율 계산
            double koreanRatio = (double)koreanCount / totalChars;
            double japaneseRatio = (double)japaneseCount / totalChars;
            double chineseRatio = (double)chineseCount / totalChars;
            double cyrillicRatio = (double)cyrillicCount / totalChars;
            double arabicRatio = (double)arabicCount / totalChars;
            double thaiRatio = (double)thaiCount / totalChars;
            double hebrewRatio = (double)hebrewCount / totalChars;
            double greekRatio = (double)greekCount / totalChars;
            double vietnameseRatio = (double)vietnameseCount / totalChars;
            double latinRatio = (double)latinCount / totalChars;

            // 고유 문자 체계가 있는 언어 우선 확인
            var priorityLanguages = new[]
            {
                (DetectedLanguage.Korean, koreanRatio),
                (DetectedLanguage.Japanese, japaneseRatio),
                (DetectedLanguage.ChineseSimplified, chineseRatio), // 간체/번체 구분은 추후
                (DetectedLanguage.Arabic, arabicRatio),
                (DetectedLanguage.Thai, thaiRatio),
                (DetectedLanguage.Hebrew, hebrewRatio),
                (DetectedLanguage.Greek, greekRatio),
                (DetectedLanguage.Vietnamese, vietnameseRatio),
            };

            var maxPriority = priorityLanguages.OrderByDescending(x => x.Item2).First();
            if (maxPriority.Item2 >= threshold)
                return maxPriority.Item1;

            // Cyrillic (Russian/Ukrainian)
            if (cyrillicRatio >= threshold)
                return DetectedLanguage.Russian; // 기본 러시아어, 우크라이나어 구분은 어려움

            // Latin 계열 언어는 일단 Latin으로 반환 (세부 구분은 어려움)
            if (latinRatio >= threshold)
                return DetectedLanguage.Latin;

            // 여러 언어가 섞여 있거나 판단 불가
            return latinRatio > 0.1 ? DetectedLanguage.English : DetectedLanguage.Unknown;
        }

        /// <summary>
        /// 텍스트가 영어인지 확인합니다.
        /// </summary>
        public static bool IsEnglish(string text)
        {
            var detected = Detect(text);
            return detected == DetectedLanguage.English || detected == DetectedLanguage.Unknown;
        }

        /// <summary>
        /// 텍스트가 특정 언어인지 확인합니다.
        /// </summary>
        public static bool IsLanguage(string text, string targetLanguageCode)
        {
            if (string.IsNullOrWhiteSpace(targetLanguageCode))
                return false;

            var detected = Detect(text);
            
            // RimWorld 언어 코드와 매칭
            var lowerCode = targetLanguageCode.ToLower();
            
            // 정확한 매칭 우선
            if (lowerCode.Contains("korean") || lowerCode.Contains("한국어"))
                return detected == DetectedLanguage.Korean;
            if (lowerCode.Contains("japanese") || lowerCode.Contains("日本語"))
                return detected == DetectedLanguage.Japanese;
            if (lowerCode.Contains("chinese") || lowerCode.Contains("简体") || lowerCode.Contains("繁體"))
                return detected == DetectedLanguage.ChineseSimplified || detected == DetectedLanguage.ChineseTraditional;
            if (lowerCode.Contains("russian") || lowerCode.Contains("русский"))
                return detected == DetectedLanguage.Russian;
            if (lowerCode.Contains("ukrainian") || lowerCode.Contains("українська"))
                return detected == DetectedLanguage.Ukrainian || detected == DetectedLanguage.Russian; // Cyrillic
            if (lowerCode.Contains("arabic"))
                return detected == DetectedLanguage.Arabic;
            if (lowerCode.Contains("thai"))
                return detected == DetectedLanguage.Thai;
            if (lowerCode.Contains("hebrew"))
                return detected == DetectedLanguage.Hebrew;
            if (lowerCode.Contains("greek") || lowerCode.Contains("ελληνικά"))
                return detected == DetectedLanguage.Greek;
            if (lowerCode.Contains("vietnamese") || lowerCode.Contains("tiếng việt"))
                return detected == DetectedLanguage.Vietnamese;
            if (lowerCode.Contains("turkish") || lowerCode.Contains("türkçe"))
                return detected == DetectedLanguage.Turkish || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("polish") || lowerCode.Contains("polski"))
                return detected == DetectedLanguage.Polish || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("czech") || lowerCode.Contains("čeština"))
                return detected == DetectedLanguage.Czech || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("german") || lowerCode.Contains("deutsch"))
                return detected == DetectedLanguage.German || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("french") || lowerCode.Contains("français"))
                return detected == DetectedLanguage.French || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("italian") || lowerCode.Contains("italiano"))
                return detected == DetectedLanguage.Italian || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("spanish") || lowerCode.Contains("español"))
                return detected == DetectedLanguage.Spanish || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("portuguese") || lowerCode.Contains("português"))
                return detected == DetectedLanguage.Portuguese || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("dutch") || lowerCode.Contains("nederlands"))
                return detected == DetectedLanguage.Dutch || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("swedish") || lowerCode.Contains("svenska"))
                return detected == DetectedLanguage.Swedish || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("norwegian") || lowerCode.Contains("norsk"))
                return detected == DetectedLanguage.Norwegian || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("danish") || lowerCode.Contains("dansk"))
                return detected == DetectedLanguage.Danish || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("finnish") || lowerCode.Contains("suomi"))
                return detected == DetectedLanguage.Finnish || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("romanian") || lowerCode.Contains("română"))
                return detected == DetectedLanguage.Romanian || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("hungarian") || lowerCode.Contains("magyar"))
                return detected == DetectedLanguage.Hungarian || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("catalan") || lowerCode.Contains("català"))
                return detected == DetectedLanguage.Catalan || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("slovak") || lowerCode.Contains("slovenčina"))
                return detected == DetectedLanguage.Slovak || detected == DetectedLanguage.Latin;
            if (lowerCode.Contains("estonian") || lowerCode.Contains("eesti"))
                return detected == DetectedLanguage.Estonian || detected == DetectedLanguage.Latin;
            
            // Latin 계열 언어는 English로 간주
            if (detected == DetectedLanguage.Latin || detected == DetectedLanguage.English)
                return lowerCode.Contains("english") || lowerCode == "en";
            
            return false;
        }

        /// <summary>
        /// 텍스트가 이미 목표 언어로 작성되어 있는지 확인합니다.
        /// 이미 목표 언어라면 번역할 필요가 없습니다.
        /// </summary>
        public static bool IsAlreadyInTargetLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // 현재 활성 언어 가져오기
            var activeLanguage = LanguageDatabase.activeLanguage;
            if (activeLanguage == null)
                return false;

            // 기본 언어(영어)인 경우 언어 감지 불필요
            if (activeLanguage == LanguageDatabase.defaultLanguage)
                return false;

            // 목표 언어 폴더 이름으로 언어 확인
            var targetLanguageFolderName = activeLanguage.folderName;
            
            return IsLanguage(text, targetLanguageFolderName);
        }

        /// <summary>
        /// 언어 코드와 표시 이름을 가져옵니다.
        /// </summary>
        public static (string code, string name, UnityEngine.Color color) GetLanguageDisplayInfo(DetectedLanguage language)
        {
            switch (language)
            {
                case DetectedLanguage.Korean:
                    return ("KO", "한국어 (Korean)", new UnityEngine.Color(0.2f, 0.6f, 1f, 0.3f));
                case DetectedLanguage.Japanese:
                    return ("JP", "日本語 (Japanese)", new UnityEngine.Color(1f, 0.4f, 0.4f, 0.3f));
                case DetectedLanguage.ChineseSimplified:
                    return ("CN", "简体中文 (Chinese Simplified)", new UnityEngine.Color(1f, 0.8f, 0.2f, 0.3f));
                case DetectedLanguage.ChineseTraditional:
                    return ("TW", "繁體中文 (Chinese Traditional)", new UnityEngine.Color(1f, 0.7f, 0.1f, 0.3f));
                case DetectedLanguage.Russian:
                    return ("RU", "Русский (Russian)", new UnityEngine.Color(0.6f, 0.2f, 1f, 0.3f));
                case DetectedLanguage.Ukrainian:
                    return ("UK", "Українська (Ukrainian)", new UnityEngine.Color(0.4f, 0.6f, 1f, 0.3f));
                case DetectedLanguage.Arabic:
                    return ("AR", "العربية (Arabic)", new UnityEngine.Color(0.8f, 0.6f, 0.2f, 0.3f));
                case DetectedLanguage.Thai:
                    return ("TH", "ไทย (Thai)", new UnityEngine.Color(0.9f, 0.3f, 0.7f, 0.3f));
                case DetectedLanguage.Hebrew:
                    return ("HE", "עברית (Hebrew)", new UnityEngine.Color(0.5f, 0.5f, 1f, 0.3f));
                case DetectedLanguage.Greek:
                    return ("GR", "Ελληνικά (Greek)", new UnityEngine.Color(0.3f, 0.5f, 0.9f, 0.3f));
                case DetectedLanguage.Vietnamese:
                    return ("VI", "Tiếng Việt (Vietnamese)", new UnityEngine.Color(0.9f, 0.5f, 0.3f, 0.3f));
                case DetectedLanguage.Turkish:
                    return ("TR", "Türkçe (Turkish)", new UnityEngine.Color(0.8f, 0.3f, 0.3f, 0.3f));
                case DetectedLanguage.Polish:
                    return ("PL", "Polski (Polish)", new UnityEngine.Color(0.9f, 0.1f, 0.3f, 0.3f));
                case DetectedLanguage.Czech:
                    return ("CZ", "Čeština (Czech)", new UnityEngine.Color(0.2f, 0.4f, 0.8f, 0.3f));
                case DetectedLanguage.German:
                    return ("DE", "Deutsch (German)", new UnityEngine.Color(0.9f, 0.7f, 0.1f, 0.3f));
                case DetectedLanguage.French:
                    return ("FR", "Français (French)", new UnityEngine.Color(0.2f, 0.3f, 0.9f, 0.3f));
                case DetectedLanguage.Italian:
                    return ("IT", "Italiano (Italian)", new UnityEngine.Color(0.1f, 0.7f, 0.3f, 0.3f));
                case DetectedLanguage.Spanish:
                    return ("ES", "Español (Spanish)", new UnityEngine.Color(0.9f, 0.5f, 0.1f, 0.3f));
                case DetectedLanguage.Portuguese:
                    return ("PT", "Português (Portuguese)", new UnityEngine.Color(0.1f, 0.6f, 0.3f, 0.3f));
                case DetectedLanguage.Dutch:
                    return ("NL", "Nederlands (Dutch)", new UnityEngine.Color(0.9f, 0.4f, 0.1f, 0.3f));
                case DetectedLanguage.Swedish:
                    return ("SV", "Svenska (Swedish)", new UnityEngine.Color(0.3f, 0.6f, 0.9f, 0.3f));
                case DetectedLanguage.Norwegian:
                    return ("NO", "Norsk (Norwegian)", new UnityEngine.Color(0.2f, 0.5f, 0.8f, 0.3f));
                case DetectedLanguage.Danish:
                    return ("DA", "Dansk (Danish)", new UnityEngine.Color(0.8f, 0.2f, 0.2f, 0.3f));
                case DetectedLanguage.Finnish:
                    return ("FI", "Suomi (Finnish)", new UnityEngine.Color(0.3f, 0.5f, 0.9f, 0.3f));
                case DetectedLanguage.Romanian:
                    return ("RO", "Română (Romanian)", new UnityEngine.Color(0.8f, 0.5f, 0.2f, 0.3f));
                case DetectedLanguage.Hungarian:
                    return ("HU", "Magyar (Hungarian)", new UnityEngine.Color(0.5f, 0.8f, 0.3f, 0.3f));
                case DetectedLanguage.Catalan:
                    return ("CA", "Català (Catalan)", new UnityEngine.Color(0.9f, 0.6f, 0.2f, 0.3f));
                case DetectedLanguage.Slovak:
                    return ("SK", "Slovenčina (Slovak)", new UnityEngine.Color(0.3f, 0.4f, 0.8f, 0.3f));
                case DetectedLanguage.Estonian:
                    return ("ET", "Eesti (Estonian)", new UnityEngine.Color(0.2f, 0.6f, 0.8f, 0.3f));
                case DetectedLanguage.English:
                    return ("EN", "English", new UnityEngine.Color(0.3f, 0.8f, 0.3f, 0.3f));
                case DetectedLanguage.Latin:
                    return ("LA", "Latin-based", new UnityEngine.Color(0.6f, 0.6f, 0.6f, 0.3f));
                case DetectedLanguage.Mixed:
                    return ("MX", "Mixed", new UnityEngine.Color(0.7f, 0.7f, 0.7f, 0.3f));
                default: // Unknown
                    return ("?", "Unknown", new UnityEngine.Color(0.5f, 0.5f, 0.5f, 0.2f));
            }
        }

        /// <summary>
        /// 디버그용: 텍스트의 언어 정보를 상세히 출력합니다.
        /// </summary>
        public static string GetLanguageInfo(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "Empty text";

            var detected = Detect(text);
            var trimmedText = Regex.Replace(text, @"\s+", "");
            int totalChars = trimmedText.Length;
            
            if (totalChars == 0)
                return "Empty text";
            
            // Count characters for all supported language scripts
            var languageCounts = new System.Collections.Generic.List<(string code, string name, int count)>
            {
                ("KR", "Korean", KoreanRegex.Matches(text).Count),
                ("JP", "Japanese", JapaneseRegex.Matches(text).Count),
                ("CN", "Chinese", ChineseRegex.Matches(text).Count),
                ("CY", "Cyrillic", CyrillicRegex.Matches(text).Count),
                ("AR", "Arabic", ArabicRegex.Matches(text).Count),
                ("TH", "Thai", ThaiRegex.Matches(text).Count),
                ("HE", "Hebrew", HebrewRegex.Matches(text).Count),
                ("GR", "Greek", GreekRegex.Matches(text).Count),
                ("VI", "Vietnamese", VietnameseRegex.Matches(text).Count),
                ("LA", "Latin", LatinRegex.Matches(text).Count)
            };
            
            // Filter to only show languages with count >= 1
            var activeLanguages = languageCounts
                .Where(l => l.count >= 1)
                .OrderByDescending(l => l.count)
                .Select(l => $"{l.code}: {l.count} ({(double)l.count/totalChars:P0})")
                .ToList();
            
            var languageInfo = activeLanguages.Count > 0 
                ? string.Join(", ", activeLanguages)
                : "No recognizable characters";
            
            return $"Detected: {detected} | Total: {totalChars} chars | {languageInfo}";
        }
    }
}

