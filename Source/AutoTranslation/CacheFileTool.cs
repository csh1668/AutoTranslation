using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Verse;

namespace AutoTranslation
{
    public static class CacheFileTool
    {
        public static string CacheDirectory
        {
            get
            {
                var path = Path.Combine(GenFilePaths.SaveDataFolderPath, "AutoTranslation");
                var directoryInfo = new DirectoryInfo(path);
                if (!directoryInfo.Exists)
                {
                    directoryInfo.Create();
                }
                return path;
            }
        }

        public static void Export(string name, Dictionary<string, string> cache)
        {
            var doc = new XmlDocument();
            Exception firstError = null;
            var errorCount = 0;
            doc.AppendElement(name, e =>
            {
                e.AppendAttribute("Language", LanguageDatabase.activeLanguage?.FriendlyNameEnglish ?? "NULL");
                var lst = cache.ToList();
                lst.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.Ordinal));
                foreach (var (k, v) in lst)
                {
                    var key = StripInvalidXmlChars(k);
                    if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(v))
                        continue;
                    if (char.IsDigit(key[0])) continue;
                    try
                    {
                        e.AppendElement(key, v);
                    }
                    catch (Exception ex)
                    {
                        firstError = firstError ?? ex;
                        errorCount++;
                    }
                }
            });
            doc.Save(Path.Combine(CacheDirectory, $"{name}.xml"));

            if (errorCount > 0)
            {
                var msg = AutoTranslation.LogPrefix +
                          $"Error on exporting cache: {firstError.Message}:{firstError.StackTrace} and {errorCount - 1} more errors.";
                Log.ErrorOnce(msg, msg.GetHashCode());
            }
        }

        public static IEnumerable<KeyValuePair<string, string>> Import(string name)
        {
            var path = Path.Combine(CacheDirectory, $"{name}.xml");
            if (!File.Exists(path)) yield break;
            var res = new List<KeyValuePair<string, string>>();
            try
            {
                var doc = new XmlDocument();
                doc.Load(path);
                foreach (var element in doc.DocumentElement.ChildNodes.OfType<XmlElement>())
                {
                    res.Add(new KeyValuePair<string, string>(element.Name, element.InnerText));
                }
            }
            catch (Exception e)
            {
                Log.Error(AutoTranslation.LogPrefix + $"Error on importing cache named {name}: {e.Message}");
            }
            foreach (var keyValuePair in res) yield return keyValuePair;
        }

        public static void Delete(string name)
        {
            var path = Path.Combine(CacheDirectory, $"{name}.xml");
            if (File.Exists(path))
                File.Delete(path);
        }

        private static string StripInvalidXmlChars(string text)
        {
            var validXmlChars = text.Where(XmlConvert.IsNCNameChar).ToArray();
            return new string(validXmlChars);
        }
    }
}
