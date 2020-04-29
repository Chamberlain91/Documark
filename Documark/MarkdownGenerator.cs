using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Documark
{
    internal sealed class MarkdownGenerator : Generator
    {
        // Two or more newlines
        private static readonly Regex _newlineCollapse = new Regex("\n(\n+)", RegexOptions.Compiled);

        private readonly Dictionary<string, int> _links = new Dictionary<string, int>();

        public MarkdownGenerator(string directory)
            : base(directory, "md")
        { }

        protected override void BeginDocument(ref string text)
        {
            // Clears link table
            _links.Clear();
        }

        protected override void EndDocument(ref string text)
        {
            // Append link metadata
            text += GenerateLinks();

            // Collapse 2+ newlines into exactly 2 newlines.
            // This is to prettify the document.
            text = _newlineCollapse.Replace(text, "\n\n");
            text = text.Replace("\t", "    ");

            string GenerateLinks()
            {
                var links = "";

                foreach (var (target, index) in _links)
                {
                    links += $"[{index}]: {target}\n";
                }

                return links;
            }
        }

        #region Document Styles

        protected override string Escape(string text)
        {
            text = text?.Replace("<", "\\<");
            text = text?.Replace("|", "\\|");
            return text;
        }

        protected override string Badge(string text)
        {
            return InlineCode(text);
        }

        protected override string Link(string text, string target)
        {
            var current = Path.GetDirectoryName(CurrentPath);
            target = Path.GetRelativePath(current, target);
            target = target.SanitizePath();

            return $"[{Escape(text)}][{GetLinkIndex()}]";

            int GetLinkIndex()
            {
                if (!_links.TryGetValue(target, out var index))
                {
                    _links[target] = (index = _links.Count);
                }
                return index;
            }
        }

        protected override string Preformatted(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            return string.Join(null, text.Split('\n').Select(s => $"    {s}"));
        }

        protected override string QuoteIndent(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            return string.Join(null, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => $"> {s}\n"));
        }

        protected override string Italics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            return $"_{text}_";
        }

        protected override string Bold(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            return $"**{text}**";
        }

        protected override string InlineCode(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            return $"`{text}`";
        }

        protected override string Table(string headerLeft, string headerRight, IEnumerable<(string left, string right)> rows)
        {
            if (rows.Any())
            {
                int lSize = headerLeft.Length, rSize = headerRight.Length;

                // Measure Rows
                foreach (var (left, right) in rows)
                {
                    lSize = Math.Max(lSize, left.Length);
                    rSize = Math.Max(rSize, right.Length);
                }

                var markdown = "";
                markdown += $"| {Str(headerLeft, lSize)} | {Str(headerRight, rSize)} |\n";
                markdown += $"|{Rep('-', lSize + 2)}|{Rep('-', rSize + 2)}|\n";

                foreach (var (left, right) in rows)
                {
                    markdown += $"| {Str(left, lSize)} | {Str(right, rSize)} |\n";
                }

                return markdown + "\n";
            }
            else
            {
                return string.Empty;
            }

            static string Rep(char chr, int num)
            {
                return new string(chr, num);
            }

            static string Str(string txt, int num)
            {
                return txt + Rep(' ', num - txt.Length);
            }
        }

        protected override string UnorderedList(IEnumerable<string> items)
        {
            var text = "";
            foreach (var item in items)
            {
                text += $" - {item}\n";
            }

            return text + "\n";
        }

        protected override string Header(HeaderSize size, string text)
        {
            if (size <= 0) { throw new ArgumentException("Header type must be 1-6."); }

            var h = new string('#', (int) size);
            return $"{h} {text.Trim()}\n\n";
        }

        protected override string Code(string text, string type = "cs")
        {
            return $"```{type}\n{text.Trim()}\n```\n\n";
        }

        #endregion

        protected override string RenderPara(XElement element)
        {
            return $"{RenderElement(element)}  \n";
        }
    }
}
