using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;

namespace AutoTranslation.Translators
{
    public interface ITranslator
    {
        string Name { get; }

        bool Ready { get; set; }
        bool RequiresKey { get; }

        TranslatorSettings Settings { get; set; }

        void Prepare();
        bool TryTranslate(string text, out string translated);
        bool TryTranslate(string text, out string translated, bool skipRetry);

        void DrawSettings(Listing_Standard ls);
    }
}
