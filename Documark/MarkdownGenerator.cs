using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Documark
{
    internal sealed class MarkdownGenerator : Generator
    {
        private readonly Dictionary<string, int> _links = new Dictionary<string, int>();

        public MarkdownGenerator() : base("md")
        { }

        protected override string GenerateAssemblyDocument()
        {
            ResetLinkTable(); // clears target -> index mapping

            var text = Header(1, Escape(GetName(CurrentAssembly)));
            text += Block(GenerateAssemblyHeader(CurrentAssembly));
            text += Divider();

            // Generate TOC
            foreach (var namespaceGroup in Types.GroupBy(t => t.Namespace).OrderBy(g => g.Key))
            {
                text += Header(2, $"{namespaceGroup.Key}");

                foreach (var typeGroup in namespaceGroup.GroupBy(t => GetTypeType(t)).OrderBy(g => g.Key))
                {
                    text += Header(3, $"{typeGroup.Key}");
                    foreach (var type in typeGroup.OrderBy(t => GetTypeSortKey(t)))
                    {
                        text += $"{Link(GetName(type), GetPath(type))}  \n";
                    }

                    text = Paragraph(text);
                }
            }

            text += GenerateLinks();
            return text;

            string GetTypeSortKey(Type type)
            {
                // Has base type, is not object and is not a value type
                return type.BaseType != null && type.BaseType != typeof(object) && !type.IsValueType
                       ? GetName(type.BaseType) + "_" + GetName(type)
                       : GetName(type);
            }
        }

        protected override string GenerateTypeDocument()
        {
            ResetLinkTable(); // clears target -> index mapping

            var text = GenerateTypeSummary();
            text += Divider();

            if (CurrentType.IsDelegate())
            {
                // Generate Body for Delegate
                text += GenerateDelegateBody();
            }
            else if (CurrentType.IsEnum)
            {
                // Generate Body for Enum
                text += GenerateEnumBody();
            }
            else
            {
                // Generate Body for Objects
                text += GenerateObjectBody();
            }

            text = Paragraph(text);
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
            var header = GenerateAssemblyHeader(CurrentAssembly);
            header += Bold("Namespace") + ": " + Link(CurrentType.Namespace, GetPath(CurrentType.Assembly)) + "  \n";
            header += Bold("Type") + ": " + Link(GetName(firstMember.DeclaringType), GetPath(firstMember.DeclaringType)) + "  \n";
            text += Block(header);
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
            text += Paragraph(GetSummary(field));
            text += Paragraph(Code(GetSyntax(field)));
            text += Paragraph(GetRemarks(field));
            text += Paragraph(GetExample(field));
            return text;
        }

        private string GenerateProperty(PropertyInfo property)
        {
            var text = "";
            text += $"{Header(3, GetName(property))}";
            text += Paragraph(GetSummary(property));
            text += Paragraph(Code(GetSyntax(property)));
            text += Paragraph(GetRemarks(property));
            text += Paragraph(GetExample(property));
            return text.Trim();
        }

        private string GenerateEvent(EventInfo @event)
        {
            var text = "";
            text += $"{Header(4, GetName(@event))}\n";
            text += Paragraph(GetSummary(@event));
            text += Paragraph($"Type: {InlineCode(GetName(@event.EventHandlerType))}");
            text += Paragraph(GetRemarks(@event));
            text += Paragraph(GetExample(@event));
            return text;
        }

        private string GenerateMethod(MethodBase method)
        {
            var text = "";
            text += Header(3, GetSignature(method, true));
            text += Paragraph(GetSummary(method));
            text += Paragraph(Code(GetSyntax(method)));
            // todo: method paramters
            text += Paragraph(GetRemarks(method));
            text += Paragraph(GetExample(method));
            return text;
        }

        private string GenerateAssemblyHeader(Assembly assembly)
        {
            var assemblyHeader = "";

            assemblyHeader += Bold("Framework") + ": " + GetFrameworkString() + "  \n";
            assemblyHeader += Bold("Assembly") + ": " + $"{Link(GetName(assembly), GetPath(assembly))}  \n";

            var dependencies = GetDependencies();
            if (dependencies.Any())
            {
                assemblyHeader += Bold("Dependencies") + ": " + string.Join(", ", dependencies) + "  \n";
            }

            return assemblyHeader;
        }

        private string GenerateObjectBody()
        {
            var text = "";

            var inherits = GetInherits(CurrentType);
            if (inherits.Any())
            {
                text += Bold("Inherits") + ": ";
                text += Paragraph(string.Join(", ", inherits.Select(t =>
                {
                    if (Documentation.IsLoaded(t)) { return Link(GetName(t), GetPath(t)); }
                    else { return Escape(GetName(t)); }
                })));
            }

            // Emit Instance Member Summary

            if (InstanceFields.Count > 0)
            {
                text += Bold("Fields") + ": ";
                text += GenerateMemberList(InstanceFields);
                text += "\n";
            }

            if (InstanceProperties.Count > 0)
            {
                text += Bold("Properties") + ": ";
                text += GenerateMemberList(InstanceProperties);
                text += "\n";
            }

            if (InstanceMethods.Count > 0)
            {
                text += Bold("Methods") + ": ";
                text += GenerateMemberList(InstanceMethods);
                text += "\n";
            }

            if (InstanceEvents.Count > 0)
            {
                text += Bold("Events") + ": ";
                text += GenerateMemberList(InstanceEvents);
                text += "\n";
            }

            // Emit Static Member Summary

            if (StaticFields.Count > 0)
            {
                text += Bold("Static Fields") + ": ";
                text += GenerateMemberList(StaticFields);
                text += "\n";
            }

            if (StaticProperties.Count > 0)
            {
                text += Bold("Static Properties") + ": ";
                text += GenerateMemberList(StaticProperties);
                text += "\n";
            }

            if (StaticMethods.Count > 0)
            {
                text += Bold("Static Methods") + ": ";
                text += GenerateMemberList(StaticMethods);
                text += "\n";
            }

            if (StaticEvents.Count > 0)
            {
                text += Bold("Static Events") + ": ";
                text += GenerateMemberList(StaticEvents);
                text += "\n";
            }

            // Cleanup newlines and add divider
            text = text.Trim() + "\n";
            text += Divider();

            if (Constructors.Any())
            {
                text += Header(2, "Constructors");
                foreach (var constructor in Constructors)
                {
                    text += Paragraph(GenerateMethod(constructor));
                }
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
            return Table("Name", "Summary", members.Select(f => (Link(GetName(f), GetPath(f)), Escape(GetSummary(f).NormalizeSpaces()))));
        }

        private string GenerateMemberList(IEnumerable<MemberInfo> members)
        {
            return $"{string.Join(", ", members.Select(d => Link(GetName(d), GetPath(d))).Distinct())}\n";
        }

        private string GenerateTypeSummary()
        {
            var text = Header(1, Escape(GetName(CurrentType)));
            var header = GenerateAssemblyHeader(CurrentAssembly) + "\n";
            header += Bold("Namespace") + ": " + Link(CurrentType.Namespace, GetPath(CurrentType.Assembly)) + "  \n";
            text += Block(header) + "\n";

            // Summary Tag
            text += Paragraph(GetSummary(CurrentType));

            // Emit Type Example
            text += $"{Code(GetSyntax(CurrentType))}\n";

            // 
            text += Paragraph(GetRemarks(CurrentType));
            text += Paragraph(GetExample(CurrentType));

            return text.Trim() + "\n";
        }

        private string GenerateDelegateBody()
        {
            return "DELEGATE";
        }

        private string GenerateEnumBody()
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
            var text = "";

            foreach (var (target, index) in _links)
            {
                text += $"[{index}]: {target}\n";
            }

            return text;
        }

        #region Document Styles

        private static string Paragraph(string text)
        {
            if (string.IsNullOrEmpty(text)) { return string.Empty; }
            else { return text.TrimEnd() + "\n\n"; }
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
            return $"\n{new string('-', 80)}\n\n";
        }

        protected override string Link(string text, string target)
        {
            var current = Path.GetDirectoryName(CurrentPath);
            target = Path.GetRelativePath(current, target);

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

                return Paragraph(markdown);
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
            return Paragraph($"{h} {text.Trim()}");
        }

        protected override string Code(string text, string type = "cs")
        {
            return Paragraph($"```{type}\n{text.Trim()}\n```");
        }

        #endregion

        protected override string RenderPara(XElement element)
        {
            return $"{RenderElement(element)}  \n";
        }
    }
}
