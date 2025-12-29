using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoTranslation
{
    public class APIKeyRotater
    {
        //private readonly Queue<string> keys = new Queue<string>();
        private readonly string[] keys;
        private int _index = 0;

        public APIKeyRotater(IEnumerable<string> keys)
        {
            this.keys = keys.Select(key => key.Trim()).ToArray();

            if (this.keys.Length == 0)
            {
                throw new ArgumentException("No keys provided");
            }
        }

        public string Key
        {
            get
            {
                var key = keys[_index];
                Rotate();
                return key;
            }
        }

        public string KeyNoRotate => keys[_index];
        public int Count => keys.Length;

        public void Rotate()
        {
            _index = (_index + 1) % keys.Length;
        }
    }
}
