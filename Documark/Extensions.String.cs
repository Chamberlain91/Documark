using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Documark
{
    public static class StringExtensions
    {
        private static readonly Regex _spaces = new Regex(@"\s+", RegexOptions.Compiled);

        public static string ToHeaderString(this string str)
        {
            str = str.ToSnakeCase();
            return string.Join(' ', str.Split('_').Select(s => Capitalize(s)));


            static string Capitalize(string s)
            {
                if (s == "as") { return s; }
                if (s == "for") { return s; }
                return s.Length > 1 ? char.ToUpper(s[0]) + s.Substring(1)
                                    : s.ToUpper();
            }
        }

        public static string ToSnakeCase(this string str)
        {
            var text = "";

            for (var i = 0; i < str.Length; i++)
            {
                if (i > 0 && char.IsLower(str[i - 1]) && char.IsUpper(str[i]))
                {
                    text += "_";
                }

                text += char.ToLower(str[i]);
            }

            return text;

        }

        public static string SanitizePath(this string path)
        {
            // 
            path = path.Replace("\\<", "[");
            path = path.Replace('<', '[');
            path = path.Replace('>', ']');
            path = path.Replace(" ", string.Empty);

            // 
            var filename = Path.GetFileName(path);
            foreach (var ch in Path.GetInvalidFileNameChars())
            {
                filename = filename.Replace(ch, '_');
            }

            // 
            var dirname = Path.GetDirectoryName(path);
            foreach (var ch in Path.GetInvalidPathChars())
            {
                dirname = dirname.Replace(ch, '_');
            }

            // 
            return Path.Combine(dirname, filename)
                       .Replace("\\", "/");
        }

        public static string NormalizeSpaces(this string text)
        {
            return _spaces.Replace(text, " ");
        }

        public static string Shorten(this string text, int maxLength = 100)
        {
            text = text.Replace("\r", " ");
            text = text.Replace("\n", " ");

            if (text.Length > maxLength)
            {
                text = $"{text.Substring(0, maxLength - 3)}...";
            }

            return text;
        }
    }
}
