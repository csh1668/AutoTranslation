# Changelog - Auto Translation

## Version 3.1.0

### 🎉 New Features

#### Language Detection System
- **Smart Language Detection**: Automatically detects if text is already in the target language
  - Prevents re-translating content that's already in Korean/Japanese/Chinese etc.
  - Handles cases where non-English content is stored in the "English" folder
  - Unicode-based detection for major languages (Korean, Japanese, Chinese, Russian, Arabic, Thai, Hebrew)
  - Configurable: Can be disabled in General settings (default: Enabled)
  - **Use Case**: Many mods store Korean text in the English folder structure, causing the auto-translator to unnecessarily translate Korean → Korean

#### Benefits
- **Reduced API Costs**: Skip translation for already-translated content
- **Better Quality**: Preserves original translations instead of round-tripping through translation APIs
- **Faster Processing**: Immediate skip without network calls

#### Translation Data Editor Enhancement
- **Language Detection Display**: Shows detected language for each translation entry
  - **All RimWorld Languages Supported**: 29+ languages with unique color-coded badges
    - Asian: Korean (KO), Japanese (JP), Chinese Simplified (CN), Chinese Traditional (TW), Thai (TH), Vietnamese (VI)
    - Cyrillic: Russian (RU), Ukrainian (UK)
    - European: German (DE), French (FR), Italian (IT), Spanish (ES), Portuguese (PT), Dutch (NL), Polish (PL), Czech (CZ), Romanian (RO), Hungarian (HU), Greek (GR), Turkish (TR), Swedish (SV), Norwegian (NO), Danish (DA), Finnish (FI), Catalan (CA), Slovak (SK), Estonian (ET)
    - Middle Eastern: Arabic (AR), Hebrew (HE)
    - Generic: English (EN), Latin-based (LA), Mixed (MX), Unknown (?)
  - Tooltip with detailed language analysis
  - Both original and translated text show their detected language
  - Helps debug and verify the language detection feature
  
- **Accordion View by Mod**: Always organized by mod with collapsible groups
  - No toggle needed - groups are always visible
  - Click group header to expand/collapse (accordion style)
  - Visual indicators: ▶ (collapsed) / ▼ (expanded)
  - "Expand All" / "Collapse All" buttons for quick control
  - Shows mod PackageId with translation count per group
  - Indented entries for better visual hierarchy
  - Hover effect on group headers for better UX
  
- **Keep Original on Empty Edit**: Smart editing behavior
  - When editing a translation, leaving it blank preserves the original text
  - Prevents accidental deletion of translations
  - Useful for reverting to original without manual copy-paste

### 🔧 Settings
- **General Tab**: Added "Enable language detection" checkbox
  - Default: Enabled (recommended)
  - Tooltip: Explains the feature and when to use it

### 🎨 UI Improvements
- **Translation Editor**: 
  - Wider window (800px → 1100px) to accommodate new features
  - Column headers for better clarity
  - Dynamic language badges with 29+ languages support
  - Detailed language info in tooltips
  - Accordion-style mod grouping (always active)
  - Interactive group headers with expand/collapse
  - Expand All / Collapse All buttons
  - Indented entries under groups (20px)
  - Visual mod group headers with entry counts
  - Hover effects for better interaction feedback

---

## Version 3.0.0

### 🎉 New Features

#### Unified Network System
- **Centralized API Request Handler**: All translators now use a unified `NetworkHelper` class
  - Robust retry logic with exponential backoff for HTTP 429 (Rate Limit) and timeouts
  - Consistent error handling across all translators
  - Configurable retry attempts (default: 3 retries)
  - Automatic 30-second timeout for all requests

#### Placeholder Protection System
- **Smart Placeholder Handling**: Prevents AI from breaking formatting
  - Protects `{0}`, `{PlayerName}`, `[itemLabel]`, `<color>` tags, and escape sequences
  - Converts placeholders to safe tokens (`__PH0__`, `__PH1__`, etc.) before translation
  - Restores original placeholders after translation with validation
  - Automatic retry (up to 2 times) if placeholder validation fails
- **Validation**: Ensures translated text maintains the same number of placeholders as the original

#### Enhanced AI Translation Quality
- **Improved Base Prompt**: More explicit instructions for AI models
  ```
  CRITICAL RULES:
  1. PRESERVE all tokens in the format __PH[number]__ exactly as they appear.
  2. Do NOT translate, remove, or modify __PH[number]__ tokens.
  3. Output ONLY the translated text, no explanations.
  4. Maintain the same tone and formality as the original.
  ```
- **Retry Logic**: Up to 2 automatic retries for failed translations
  - Validates placeholder count and restoration
  - 500ms delay between retries
  - Falls back to original text after all retries fail

#### New Translators
- **OpenAI Compatible**: Supports local and self-hosted AI models
  - Works with Ollama, LM Studio, LocalAI, and generic OpenAI-compatible APIs
  - Configurable base URL (default: `http://localhost:11434/v1/`)
  - Optional API key (not required for most local models)
- **LibreTranslate**: Free, no API key required
  - Default public instance: `https://libretranslate.com`
  - Supports self-hosted instances
  - Optional API key for premium features
  - Reliable alternative to Google Translate

#### Translation Data Editor
- **In-game Editor**: Manage translation data directly in the game without opening folders
  - Search: Search in both original and translated text
  - Edit: Fix incorrect translations immediately
  - Delete: Remove unnecessary translation entries
- **Access**: Mod Settings → Advanced Tab → "Translation Data Editor" button

#### Improved Settings UI
- **Tab-based Interface**: Organized into General, Translator, Target Mods, and Advanced tabs
- **Concurrency Control**: Configure max concurrent translations (1-20)
- **Better Translation Testing**: Improved per-translator test functionality

### 🔧 Major Improvements

#### Translator Refactoring
All existing translators have been refactored to use the new systems:
- **ChatGPT, Claude, Gemini**: Now use unified `NetworkHelper` with placeholder protection
- **Google Translate**: Enhanced with placeholder protection system
- **DeepL**: 
  - Migrated from `UnityWebRequest` to `NetworkHelper` for consistency
  - Uses unified placeholder protection (replaces legacy XML tag method)
  - Simplified retry logic through NetworkHelper
- **LibreTranslate**: New free translator with placeholder protection

#### Settings System Enhancement
- **Translator-Specific Settings**: New settings classes for each translator type
  - `TranslatorSettings_AIModel`: For AI-based translators (ChatGPT, Claude, etc.)
  - `TranslatorSettings_DeepL`: Dedicated settings for DeepL
  - `TranslatorSettings_LibreTranslate`: Custom URL and optional API key
- **Better Property Management**: Virtual `RequiresKey` property for flexible key requirements

### 📊 Performance Impact
- **Network Requests**: Retry logic may increase request time for failing requests
- **Placeholder Processing**: Adds ~1-5ms per translation (negligible)
- **Memory**: Increases by ~2-5MB for placeholder caching

### 🌐 Compatibility
- ✅ RimWorld 1.4
- ✅ RimWorld 1.5
- ✅ RimWorld 1.6
- ✅ All existing save files and settings

## Previous Versions

### Version 2.x
- Basic translation functionality
- Support for major translators: Google, ChatGPT, Claude, etc.
- DefInjection and Keyed translation

---

## Contributing

This project is open source. Contributions are welcome!
- GitHub: [Project Link]
- Steam Workshop: [Workshop Link]
