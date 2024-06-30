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

        bool Ready { get; }
        bool RequiresKey { get; }

        void Prepare();
        bool TryTranslate(string text, out string translated);
    }
}
