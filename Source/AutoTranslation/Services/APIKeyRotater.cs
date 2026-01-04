using System;
using System.Collections.Generic;

namespace AutoTranslation.Services
{
    public class APIKeyRotater
    {
        private readonly Queue<string> keys = new Queue<string>();

        public APIKeyRotater(IEnumerable<string> keys)
        {
            foreach (var key in keys)
            {
                this.keys.Enqueue(key.Trim());
            }

            if (this.keys.Count == 0)
            {
                throw new ArgumentException("No keys provided");
            }
        }

        public string Key
        {
            get
            {
                var key = keys.Peek();
                Rotate();
                return key;
            }
        }

        public string KeyNoRotate => keys.Peek();
        public int Count => keys.Count;

        public void Rotate()
        {
            keys.Enqueue(keys.Dequeue());
        }
    }
}

