using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Documark
{
    internal sealed class MarkdownGenerator : Generator
    {
        private readonly Dictionary<string, int> _links = new Dictionary<string, int>();

        public MarkdownGenerator() : base("md")
        { }

        protected override string GenerateTypeDocument()
        {
            ResetLinkTable(); // clears target -> index mapping

            // Get Type Header
            var text = GenerateTypeSummary();
            text += Divider();

            if (CurrentType.IsDelegate())
            {
                // Generate delegate types
                text += GenerateDelegate();
            }
            else if (CurrentType.IsEnum)
            {
                // Generate delegate types
                text += GenerateEnum();
            }
            else
            { 
                // Generate member summary
                text += GenerateMemberSummary();
            }

            text += GenerateLinks();
            return text;
        }

        protected override string GenerateMembersDocument(IEnumerable<MemberInfo> members)
        {
            ResetLinkTable(); // clears target -> index mapping

            // Validate members are from the same consistent type
            var firstMember = members.First();
            if (!members.All(m => m.DeclaringType == firstMember.DeclaringType))
            {
                throw new InvalidOperationException("All members must be from the same type.");
            }

            var text = Header(1, Escape(GetName(firstMember.DeclaringType) + "." + GetName(firstMember)));

            //
            var header = GenerateAssemblyHeader(firstMember.DeclaringType);
            header += Bold("Declaring Type") + ": " + Link(GetName(firstMember.DeclaringType), GetPath(firstMember.DeclaringType)) + "  \n";
            text += Block(header) + "\n";
            text += Divider();

            // Write members to document
            foreach (var member in members)
            {
                switch (member)
                {
                    case FieldInfo field:
                        text += GenerateField(field);
                        break;

                    case PropertyInfo property:
                        text += GenerateProperty(property);
                        break;

                    case EventInfo @event:
                        text += GenerateEvent(@event);
                        break;

                    case MethodInfo method:
                        text += GenerateMethod(method);
                        break;
                }

                text = Paragraph(text);
            }

            // Append links metadata
            text += GenerateLinks();
            return text;
        }

        private string GenerateField(FieldInfo field)
        {
            var text = "";
            text += $"{Header(4, GetName(field))}";
            text += Paragraph($"Type: {InlineCode(GetName(field.FieldType))}");
            text += Paragraph(GetSummary(field));
            text += Paragraph(GetRemarks(field));
            text += Paragraph(GetExample(field));
            return text;
        }

        private string GenerateProperty(PropertyInfo property)
        {
            var text = "";
            text += $"{Header(3, GetName(property))}";
            text += Paragraph(Code(GetSyntax(property)));
            text += Paragraph(GetSummary(property));
            text += Paragraph(GetRemarks(property));
            text += Paragraph(GetExample(property));
            return text.Trim();
        }

        private string GenerateEvent(EventInfo @event)
        {
            var text = "";
            text += $"{Header(4, GetName(@event))}\n";
            text += Paragraph($"Type: {InlineCode(GetName(@event.EventHandlerType))}");
            text += Paragraph(GetSummary(@event));
            text += Paragraph(GetRemarks(@event));
            text += Paragraph(GetExample(@event));
            return text;
        }

        private string GenerateMethod(MethodBase method)
        {
            var text = "";
            text += $"{Header(3, GetSignature(method, true))}";
            text += Paragraph(GetSummary(method));
            text += Paragraph(Code(GetSyntax(method)));
            // todo: method paramters
            text += Paragraph(GetRemarks(method));
            text += Paragraph(GetExample(method));
            return text;
        }

        private string GenerateAssemblyHeader(Type type)
        {
            var assemblyHeader = "";
            assemblyHeader += Bold("Assembly") + ": " + $"{GetName(type.Assembly)} ({Italics(GetFrameworkString())})  \n";
            assemblyHeader += Bold("Dependancies") + ": " + GetDependencies() + "  \n";
            return assemblyHeader;
        }

        private string GenerateMemberSummary()
        {
            var text = "";

            // Emit Instance Member Summary

            if (InstanceFields.Count > 0)
            {
                text += Header(4, "Fields");
                text += GenerateMemberList(InstanceFields);
                text += "\n";
            }

            if (InstanceProperties.Count > 0)
            {
                text += Header(4, "Properties");
                text += GenerateMemberList(InstanceProperties);
                text += "\n";
            }

            if (InstanceMethods.Count > 0)
            {
                text += Header(4, "Methods");
                text += GenerateMemberList(InstanceMethods);
                text += "\n";
            }

            if (InstanceEvents.Count > 0)
            {
                text += Header(4, "Events");
                text += GenerateMemberList(InstanceEvents);
                text += "\n";
            }

            // Emit Static Member Summary

            if (StaticFields.Count > 0)
            {
                text += Header(4, "Static Fields");
                text += GenerateMemberList(StaticFields);
                text += "\n";
            }

            if (StaticProperties.Count > 0)
            {
                text += Header(4, "Static Properties");
                text += GenerateMemberList(StaticProperties);
                text += "\n";
            }

            if (StaticMethods.Count > 0)
            {
                text += Header(4, "Static Methods");
                text += GenerateMemberList(StaticMethods);
                text += "\n";
            }

            if (StaticEvents.Count > 0)
            {
                text += Header(4, "Static Events");
                text += GenerateMemberList(StaticEvents);
                text += "\n";
            }

            // Cleanup newlines and add divider
            text = text.Trim() + "\n";
            text += Divider();

            text += Header(2, "Constructors");
            foreach (var constructor in Constructors)
            {
                text += Paragraph(GenerateMethod(constructor));
            }

            if (Fields.Any())
            {
                text += Header(2, "Fields");
                text += GenerateMemberTable(Fields);
                text = text.Trim() + "\n\n";
            }

            if (Properties.Any())
            {
                text += Header(2, "Properties");
                text += GenerateMemberTable(Properties);
                text = text.Trim() + "\n\n";
            }

            if (Events.Any())
            {
                text += Header(2, "Events");
                text += GenerateMemberTable(Events);
                text = text.Trim() + "\n\n";
            }

            if (Methods.Any())
            {
                text += Header(2, "Methods");
                text += GenerateMemberTable(Methods);
                text = text.Trim() + "\n\n";
            }

            text = text.Trim() + "\n\n";
            return text;
        }

        private string GenerateMemberTable(IEnumerable<MemberInfo> members)
        {
            return Table("Name", "Summary", members.Select(f => (Link(Escape(GetName(f)), GetPath(f)), Escape(GetSummary(f).NormalizeSpaces()))));
        }

        private string GenerateMemberList(IEnumerable<MemberInfo> members)
        {
            return $"{string.Join(", ", members.Select(d => Link(Escape(GetName(d)), GetPath(d))).Distinct())}\n";
        }

        private string GenerateTypeSummary()
        {
            var text = Header(1, Escape(GetName(CurrentType)));

            text += Block(GenerateAssemblyHeader(CurrentType)) + "\n";

            // Emit Type Summary
            var documentation = Documentation.GetDocumentation(CurrentType);
            if (documentation != null)
            {
                // Summary Tag
                var summary = RenderElement(documentation?.Element("summary"));
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    text += $"{summary}\n\n";
                }

                // Remarks Tag
                var remarks = RenderElement(documentation?.Element("remarks"));
                if (!string.IsNullOrWhiteSpace(remarks))
                {
                    text += $"{remarks}\n\n";
                }
            }

            // Emit Type Example
            text += $"{Code(GetSyntax(CurrentType))}\n";

            return text.Trim() + "\n";
        }

        private string GenerateDelegate()
        {
            return "DELEGATE";
        }

        private string GenerateEnum()
        {
            var text = "";
            text += Table("Name", "Summary", Fields.Select(m => (GetName(m), GetSummary(m).NormalizeSpaces())));
            return text.Trim() + "\n";
        }

        private void ResetLinkTable()
        {
            _links.Clear();
        }

        private string GenerateLinks()
        {
            var text = "\n";

            foreach (var (target, index) in _links)
            {
                text += $"[{index}]: {target}\n";
            }

            return text;
        }

        #region Document Styles

        private static string Paragraph(string text)
        {
            return text.Trim() + "\n\n";
        }

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

        protected override string Divider()
        {
            return $"\n{new string('-', 60)}\n\n";
        }

        protected override string Link(string text, string target)
        {
            var current = Path.GetDirectoryName(GetPath(CurrentType));
            target = Path.GetRelativePath(current, target);

            return $"[{text}][{GetLinkIndex()}]";

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

        protected override string Block(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            return string.Join(null, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(s => $"> {s}\n"));
        }

        protected override string Italics(string text, string type = "cs")
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            return $"_{text}_";
        }

        protected override string Bold(string text, string type = "cs")
        {
            if (string.IsNullOrWhiteSpace(text)) { return string.Empty; }
            return $"**{text}**";
        }

        protected override string InlineCode(string text, string type = "cs")
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

                markdown += "\n";
                return markdown;
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

        protected override string Header(int size, string text)
        {
            if (size <= 0) { throw new ArgumentException("Header type must be 1-6."); }

            var h = new string('#', size);
            return $"{h} {text.Trim()}\n\n";
        }

        protected override string Code(string text, string type = "cs")
        {
            return $"```{type}\n{text.Trim()}\n```\n";
        }

        #endregion
    }
}
