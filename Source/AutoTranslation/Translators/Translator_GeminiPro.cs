﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoTranslation.Translators
{
    internal class Translator_GeminiPro : Translator_GeminiBase
    {
        protected override string Model => "gemini-1.5-pro";
        public override string Name => "Gemini 1.5 pro";
    }
}