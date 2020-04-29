using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Documark
{
    internal sealed class HtmlGenerator : Generator
    {
        // Two or more newlines
        private static readonly Regex _newlineCollapse = new Regex("\n+", RegexOptions.Compiled);

        public HtmlGenerator(string directory)
            : base(directory, "html")
        { }

        protected override void BeginDocument(ref string text)
        {
            // Nothing to do
        }

        protected override void EndDocument(ref string text)
        {
            // todo: css/template?

            var path = Path.GetRelativePath(CurrentPath, OutputDirectory + "/doc-style.css");
            path = path.SanitizePath();

            var head = $"<link href=\"{path}\" rel=\"stylesheet\">";

            // Collapse newlines into exactly 1 newlines.
            text = _newlineCollapse.Replace(text, "\n");

            text = Tag("html", Tag("head", head) + Tag("body", text));
        }

        private static string Tag(string tag, IEnumerable<string> items, params (string name, string val)[] attrs)
        {
            return Tag(tag, string.Join("", items), attrs);
        }

        private static string Tag(string tag, string text, params (string name, string val)[] attrs)
        {
            var attributes = string.Join(" ", attrs.Select(attr => $"{attr.name}=\"{attr.val}\""));
            if (!string.IsNullOrEmpty(attributes)) { attributes = " " + attributes; }
            return $"<{tag}{attributes}>{ text}</{tag}>";
        }

        #region Document Styles

        protected override string Paragraph(string text)
        {
            if (string.IsNullOrEmpty(text)) { return string.Empty; }
            else { return Tag("p", text); }
        }

        protected override string LineBreak()
        {
            return "<br/>";
        }

        protected override string Escape(string text)
        {
            // htmlspecialchars?
            text = text?.Replace("<", "&lt;");
            text = text?.Replace(">", "&gt;");
            return text;
        }

        protected override string Badge(string text)
        {
            return Tag("span", text, ("class", "doc-badge"));
        }

        protected override string Link(string text, string target)
        {
            var current = Path.GetDirectoryName(CurrentPath);
            target = Path.GetRelativePath(current, target);
            target = target.SanitizePath();

            return Tag("a", text, ("href", target));
        }

        protected override string QuoteIndent(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            return Tag("blockquote", text);
        }

        protected override string Italics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            return Tag("emphasis", text);
        }

        protected override string Bold(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            return Tag("strong", text);
        }

        protected override string InlineCode(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            return Tag("code", text);
        }

        protected override string Table(string[] headers, IEnumerable<string[]> rows)
        {
            var text = "";

            foreach (var row in rows)
            {
                text += Tag("tr", row.Select(r => Tag("td", r)));
            }

            return Tag("table", text);
        }

        protected override string UnorderedList(IEnumerable<string> items)
        {
            return Tag("ul", items.Select(i => Tag("li", i)));
        }

        protected override string Header(HeaderSize size, string text)
        {
            return Tag($"h{(int) size}", text);
        }

        protected override string Code(string text, string type = "cs")
        {
            return Tag("pre", Tag("code", text, ("doc-code-type", type)));
        }

        #endregion 
    }
}
