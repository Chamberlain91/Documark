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

            // To prettify the document, we will collapse two or
            // more newlines into exactly two newlines.
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

        protected override string LineBreak()
        {
            return $"  \n";
        }

        protected override string Paragraph(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            else { return text + "\n\n"; }
        }

        protected override string Header(HeaderSize size, string text)
        {
            if (size <= 0) { throw new ArgumentException("Header type must be 1-6."); }

            var h = new string('#', (int) size);
            return $"{h} {text.Trim()}\n\n";
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

        protected override string Code(string text, string type = "cs")
        {
            return $"```{type}\n{text.Trim()}\n```\n\n";
        }

        protected override string Badge(string text)
        {
            return InlineCode(text);
        }

        protected override string QuoteIndent(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            return string.Join(null, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => $"> {s}\n")) + "\n\n";
        }

        protected override string Link(string text, string target)
        {
            return $"[{text}][{GetLinkIndex()}]";

            int GetLinkIndex()
            {
                target = GetRelativePath(target);

                if (!_links.TryGetValue(target, out var index))
                {
                    _links[target] = (index = _links.Count);
                }

                return index;
            }
        }

        protected override string Table(string[] headers, IEnumerable<string[]> rows)
        {
            if (rows.Any())
            {
                // Compute initial sizes
                var sizes = new int[headers.Length];
                for (var col = 0; col < headers.Length; col++)
                {
                    sizes[col] = headers[col].Length;
                }

                // Measure Rows
                foreach (var row in rows)
                {
                    if (row.Length != headers.Length)
                    {
                        Console.WriteLine("ERROR: Table row inconsistent size.");
                        continue;
                    }

                    // Measure each maximal columns size
                    for (var col = 0; col < headers.Length; col++)
                    {
                        sizes[col] = Math.Max(sizes[col], row[col].Length);
                    }
                }

                var markdown = "";
                markdown += "| " + string.Join(" | ", headers.Select((s, i) => Str(s, sizes[i]))) + " |\n";
                markdown += "|-" + string.Join("-|-", headers.Select((s, i) => Rep('-', sizes[i]))) + "-|\n";

                foreach (var row in rows)
                {
                    markdown += "| " + string.Join(" | ", row.Select((s, i) => Str(s, sizes[i]))) + " |\n";
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

        protected override string Escape(string text)
        {
            text = text?.Replace("<", "\\<");
            text = text?.Replace("|", "\\|");
            return text;
        }

        #endregion
    }
}
