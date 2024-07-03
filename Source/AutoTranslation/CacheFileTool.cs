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
            doc.AppendElement(name, e =>
            {
                e.AppendAttribute("Language", LanguageDatabase.activeLanguage?.FriendlyNameEnglish ?? "NULL");
                foreach (var (k, v) in cache)
                {
                    if (string.IsNullOrEmpty(k) || string.IsNullOrEmpty(v))
                        continue;
                    try
                    {
                        e.AppendElement(k, v);
                    }
                    catch (Exception)
                    {
                        // suppress
                    }
                }
            });
            doc.Save(Path.Combine(CacheDirectory, $"{name}.xml"));
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
    }
}
