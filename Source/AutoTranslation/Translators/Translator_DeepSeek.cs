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

        public override List<string> GetModels()
        {
            try
            {
                // Try to fetch models from API first
                return base.GetModels();
            }
            catch
            {
                // Fallback to known DeepSeek models
                return new List<string>
                {
                    "deepseek-chat",
                    "deepseek-coder"
                };
            }
        }
    }
}
