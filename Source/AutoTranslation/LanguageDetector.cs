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

            // Latin 계열 언어 (English 포함)
            if (latinRatio >= threshold)
            {
                // 세부 구분 없이 English로 통일하거나 Latin으로 반환
                return DetectedLanguage.English;
            }

            // 여러 언어가 섞여 있거나 판단 불가
            return latinRatio > 0.1 ? DetectedLanguage.English : DetectedLanguage.Unknown;
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
            
            // 영어 확인
            if (lowerCode.Contains("english") || lowerCode == "en")
                return detected == DetectedLanguage.English || detected == DetectedLanguage.Latin;
            
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

            // 목표 언어 폴더 이름
            var targetLanguageFolderName = activeLanguage.folderName;
            
            // 라틴 문자권 언어(영어, 독일어, 프랑스어 등)끼리는 문자 체계가 동일하여 
            // 텍스트 분석만으로 언어를 명확히 구분하기 어렵습니다. (오탐 가능성 높음)
            // 따라서 언어 체계가 완전히 다른 경우(영어 <-> 한국어/중국어 등)에만
            // 중복 번역 방지 기능을 활성화합니다.
            if (IsNonLatinScriptLanguage(targetLanguageFolderName))
            {
                return IsLanguage(text, targetLanguageFolderName);
            }

            // 라틴 문자권 언어는 오탐 방지를 위해 감지 기능을 끄고 항상 번역을 시도합니다.
            return false;
        }

        /// <summary>
        /// 해당 언어가 비라틴 문자(한글, 한자, 키릴 문자 등)를 주로 사용하는지 확인합니다.
        /// </summary>
        private static bool IsNonLatinScriptLanguage(string languageFolderName)
        {
             var lower = languageFolderName.ToLower();
             return lower.Contains("korean") || lower.Contains("한국어") ||
                    lower.Contains("japanese") || lower.Contains("日本語") ||
                    lower.Contains("chinese") || lower.Contains("简体") || lower.Contains("繁體") ||
                    lower.Contains("russian") || lower.Contains("русский") ||
                    lower.Contains("ukrainian") || lower.Contains("українська") || // Cyrillic
                    lower.Contains("arabic") || 
                    lower.Contains("thai") || 
                    lower.Contains("hebrew") || 
                    lower.Contains("greek");
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
