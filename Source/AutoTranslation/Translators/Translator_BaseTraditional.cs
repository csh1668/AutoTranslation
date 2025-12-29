using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoTranslation.Translators
{
    public abstract class Translator_BaseTraditional : ITranslator
    {
        public abstract string Name { get; }
        public virtual bool RequiresKey { get; } = true;

        public virtual string StartLanguage => "auto";
        public abstract string TranslateLanguage { get; }

        public abstract bool TryTranslate(string text, out string translated);
        
        // Default implementation calls the original method with skipRetry=false
        public virtual bool TryTranslate(string text, out string translated, bool skipRetry)
        {
            return TryTranslate(text, out translated);
        }

        public abstract bool SupportsCurrentLanguage();
    }
}
