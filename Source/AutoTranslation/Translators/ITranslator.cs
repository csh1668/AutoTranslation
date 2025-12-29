using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoTranslation.Translators
{
    public interface ITranslator
    {
        string Name { get; }

        bool RequiresKey { get; }
        bool TryTranslate(string text, out string translated);
        bool TryTranslate(string text, out string translated, bool skipRetry);
    }
}
