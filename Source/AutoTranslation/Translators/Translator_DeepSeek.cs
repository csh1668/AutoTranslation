using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoTranslation.Translators
{
    public class Translator_DeepSeek : Translator_ChatGPT
    {
        public override string Name => "DeepSeek";

        protected override string RoleSystem => "system";

        public override string BaseURL => "https://api.deepseek.com/";
    }
}
