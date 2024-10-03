using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using AutoTranslation.Translators;

namespace AutoTranslation
{
    internal static class Helpers
    {
        public static string FitFormat(this string str, int cnt)
        {
            //str = str.Replace("\"{", "{").Replace("}\"", "}")
            //    .Replace("「{", "{").Replace("}」", "}");
            var pattern = @"\{\d+\}";
            var matches = Regex.Matches(str, pattern);
            var curCnt = matches.Count;
            if (curCnt == cnt) return str;
            if (curCnt > cnt)
            {
                return Regex.Replace(str, pattern, match => int.Parse(match.Groups[1].Value) >= cnt ? "" : match.Value);
            }
            var sb = new StringBuilder(str);
            if (curCnt < cnt)
            {
                while (curCnt < cnt) sb.Append($"|{{{curCnt++}}}|");
            }
            return sb.ToString();
        }

        public static (string, List<string>) ToFormatString(this string str)
        {
            string capture = @"[\[\{](.*?)[\]\}]";
            var placeholders = new List<string>();
            var formatString = Regex.Replace(str, capture, match =>
            {
                var placeholder = match.Groups[1].Value;
                if (match.Value.StartsWith("["))
                {
                    placeholders.Add($"[{placeholder}]");
                }
                else if (match.Value.StartsWith("{"))
                {
                    placeholders.Add($"{{{placeholder}}}");
                }
                return $"{{{placeholders.Count - 1}}}";
            });
            return (formatString, placeholders);
        }

        public static string[] Tokenize(this string str) =>
            str.Split(new[] { ".\n", ". " }, StringSplitOptions.RemoveEmptyEntries);

        #region XmlHelpers

        public static XmlElement Append(this XmlElement parent, Action<XmlElement> work)
        {
            work(parent);
            return parent;
        }

        public static XmlElement AppendElement(this XmlNode parent, string name, string innerText = null)
        {
            var child = (XmlElement)parent.AppendChild(
                (parent.NodeType == XmlNodeType.Document ? (XmlDocument)parent : parent.OwnerDocument)
                .CreateElement(name)) ?? throw new NullReferenceException();
            if (innerText != null)
            {
                child.InnerText = innerText;
            }

            return child;
        }
        public static XmlElement AppendElement(this XmlElement parent, string name, string innerText = null)
        {
            var child = (XmlElement)parent.AppendChild(parent.OwnerDocument.CreateElement(name)) ??
                        throw new NullReferenceException();

            if (innerText != null)
            {
                child.InnerText = innerText;
            }
            return child;
        }

        public static XmlElement AppendElement(this XmlNode parent, string name, Action<XmlElement> work)
        {
            var child = parent.AppendElement(name);
            work(child);
            return child;
        }

        public static XmlAttribute AppendAttribute(this XmlNode parent, string name, string value)
        {
            if (parent is XmlElement e)
                return e.AppendAttribute(name, value);
            else
                return null;
        }
        public static XmlAttribute AppendAttribute(this XmlElement parent, string name, string value)
        {
            var attr = parent.Attributes.Append(parent.OwnerDocument.CreateAttribute(name));
            if (value != null)
            {
                attr.Value = value;
            }
            return attr;
        }

        #endregion

        public static string EscapeJsonString(this string input)
        {
            var sb = new StringBuilder(input.Length);
            foreach (var c in input)
            {
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
