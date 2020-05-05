using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace Documark
{
    internal abstract class Generator
    {
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        private const BindingFlags InstanceBinding = BindingFlags.Instance | Declared;
        private const BindingFlags StaticBinding = BindingFlags.Static | Declared;

        public IReadOnlyList<Type> Types { get; private set; }

        public IReadOnlyList<ConstructorInfo> Constructors { get; private set; }

        public IReadOnlyList<FieldInfo> InstanceFields { get; private set; }
        public IReadOnlyList<PropertyInfo> InstanceProperties { get; private set; }
        public IReadOnlyList<MethodInfo> InstanceMethods { get; private set; }
        public IReadOnlyList<EventInfo> InstanceEvents { get; private set; }

        public IReadOnlyList<FieldInfo> StaticFields { get; private set; }
        public IReadOnlyList<PropertyInfo> StaticProperties { get; private set; }
        public IReadOnlyList<MethodInfo> StaticMethods { get; private set; }
        public IReadOnlyList<EventInfo> StaticEvents { get; private set; }

        public IReadOnlyList<FieldInfo> Fields { get; private set; }
        public IReadOnlyList<PropertyInfo> Properties { get; private set; }
        public IReadOnlyList<MethodInfo> Methods { get; private set; }
        public IReadOnlyList<EventInfo> Events { get; private set; }

        public IReadOnlyList<MemberInfo> InstanceMembers { get; private set; }
        public IReadOnlyList<MemberInfo> StaticMembers { get; private set; }
        public IReadOnlyList<MemberInfo> Members { get; private set; }

        public Type CurrentType { get; private set; }

        public Assembly CurrentAssembly { get; private set; }

        public string CurrentPath { get; private set; }

        internal readonly string Extension;

        internal readonly string OutputDirectory;

        protected Generator(string directory, string extension)
        {
            OutputDirectory = directory ?? throw new ArgumentNullException(nameof(directory));
            Extension = extension ?? throw new ArgumentNullException(nameof(extension));
        }

        internal void Generate(Assembly assembly)
        {
            // Delete assembly root directory (if exists) and regenerate it
            var root = GetRootDirectory(assembly);
            if (Directory.Exists(root)) { Directory.Delete(root, true); }

            // Set the current assembly
            SetCurrentAssembly(assembly);

            // Generate and write assembly file to disk
            GenerateDocument(GetPath(assembly), GenerateAssemblyDocument);

            // We will generate documents for each type
            foreach (var type in Types)
            {
                // Sets the current type and gathers type information
                SetCurrentType(type);

                // Generate and write file to disk
                GenerateDocument(GetPath(type), GenerateTypeDocument);

                // Only bother with writing member files with class/structs
                if (!type.IsEnum && !type.IsDelegate())
                {
                    // Generate member files
                    foreach (var memberGroup in Members.GroupBy(m => GetName(m)))
                    {
                        // Generate and write file to disk
                        var path = GetPath(memberGroup.First());
                        GenerateDocument(path, () => GenerateMembersDocument(memberGroup));
                    }
                }
            }

            void GenerateDocument(string path, Func<string> generateDocument)
            {
                CurrentPath = path;
                Directory.CreateDirectory(Path.GetDirectoryName(CurrentPath));

                var document = "";
                BeginDocument(ref document);
                document += generateDocument();
                EndDocument(ref document);
                File.WriteAllText(path, document);
            }

            void SetCurrentAssembly(Assembly assembly)
            {
                Types = Documentation.GetVisibleTypes(assembly).ToArray();
                CurrentAssembly = assembly;
            }

            void SetCurrentType(Type type)
            {
                CurrentType = type;

                // Get Constructor Methods
                Constructors = type.GetConstructors(InstanceBinding).Where(m => IsVisible(m)).ToArray();

                // Get Instance Members
                InstanceFields = type.GetFields(InstanceBinding).Where(m => IsVisible(m)).OrderBy(m => GetName(m)).ToArray();
                InstanceProperties = type.GetProperties(InstanceBinding).Where(m => IsVisible(m)).OrderBy(m => GetName(m)).ToArray();
                InstanceMethods = type.GetMethods(InstanceBinding).Where(m => IsVisible(m)).OrderBy(m => GetName(m)).ToArray();
                InstanceEvents = type.GetEvents(InstanceBinding).Where(m => IsVisible(m)).OrderBy(m => GetName(m)).ToArray();

                // Get Static Members
                StaticFields = type.GetFields(StaticBinding).Where(m => IsVisible(m)).OrderBy(m => GetName(m)).ToArray();
                StaticProperties = type.GetProperties(StaticBinding).Where(m => IsVisible(m)).OrderBy(m => GetName(m)).ToArray();
                StaticMethods = type.GetMethods(StaticBinding).Where(m => IsVisible(m)).OrderBy(m => GetName(m)).ToArray();
                StaticEvents = type.GetEvents(StaticBinding).Where(m => IsVisible(m)).OrderBy(m => GetName(m)).ToArray();

                // Get Concatenated Members
                Fields = InstanceFields.Concat(StaticFields).OrderBy(m => GetName(m)).ToArray();
                Properties = InstanceProperties.Concat(StaticProperties).OrderBy(m => GetName(m)).ToArray();
                Methods = InstanceMethods.Concat(StaticMethods).OrderBy(m => GetName(m)).ToArray();
                Events = InstanceEvents.Concat(StaticEvents).OrderBy(m => GetName(m)).ToArray();

                // 
                InstanceMembers = ConcatMembers(InstanceFields, InstanceProperties, InstanceMethods, InstanceEvents).ToArray();
                StaticMembers = ConcatMembers(StaticFields, StaticProperties, StaticMethods, StaticEvents).ToArray();
                Members = ConcatMembers(Fields, Properties, Methods, Events).ToArray();

                static IEnumerable<MemberInfo> ConcatMembers(IEnumerable<MemberInfo> a, IEnumerable<MemberInfo> b,
                                                             IEnumerable<MemberInfo> c, IEnumerable<MemberInfo> d)
                {
                    return a.Concat(b).Concat(c).Concat(d);
                }
            }
        }

        protected abstract void BeginDocument(ref string text);

        protected abstract void EndDocument(ref string text);

        #region Generate Document

        private string GenerateAssemblyHeader(Assembly assembly)
        {
            var header = "";
            header += $"{Bold("Framework")}: {GetFrameworkString()}" + LineBreak();
            header += $"{Bold("Assembly")}: {Link(GetName(assembly), GetPath(assembly))}";
            return header;
        }

        protected virtual string GenerateAssemblyDocument()
        {
            var text = Header(HeaderSize.Large, Escape(GetName(CurrentAssembly)));
            text += QuoteIndent(GenerateAssemblyHeader(CurrentAssembly));

            var dependencies = GetDependencies();
            if (dependencies.Any())
            {
                text += Header(HeaderSize.Medium, "Assembly Dependencies");
                text += UnorderedList(dependencies);
            }

            // Generate TOC
            // Group by namespaces
            foreach (var namespaceGroup in Types.GroupBy(t => t.Namespace).OrderBy(g => g.Key))
            {
                // Write namespace title
                text += Header(HeaderSize.Medium, $"{namespaceGroup.Key}");

                // Group by object kind (class, enum, etc)
                foreach (var typeGroup in namespaceGroup.GroupBy(t => GetObjectKind(t)).OrderBy(g => g.Key))
                {
                    // Write object kind title
                    text += Header(HeaderSize.Small, $"{typeGroup.Key}");

                    // Generate type table
                    text += Table(new[] { "Name", "Summary" }, typeGroup.OrderBy(t => GetTypeSortKey(t))
                                                              .Select(t => new[] { GetLink(t), GetSummary(t, true).Summarize() }));
                }
            }

            return text;

            string GetTypeSortKey(Type type)
            {
                // Has base type, is not object and is not a value type
                return type.BaseType != null && type.BaseType != typeof(object) && !type.IsValueType
                       ? GetName(type.BaseType) + "_" + GetName(type)
                       : GetName(type);
            }
        }

        protected virtual string GenerateTypeDocument()
        {
            var text = Header(HeaderSize.Large, Escape(GetName(CurrentAssembly)));
            text += QuoteIndent(GenerateAssemblyHeader(CurrentAssembly));

            // Write type header (namespace, name, class/struct/etc)
            text += Header(HeaderSize.Medium, Escape($"{GetName(CurrentType)} ({GetObjectKind(CurrentType)})"));
            text += QuoteIndent(Bold("Namespace") + ": " + Link(CurrentType.Namespace, GetPath(CurrentType.Assembly)));

            if (CurrentType.IsDelegate())
            {
                text += Paragraph(GetSummary(CurrentType));
                text += Code(GetDelegateSyntax(CurrentType));
                text += Paragraph(GenerateAttributeBadges(CurrentType));
                text += Paragraph(GetRemarks(CurrentType));
                text += Paragraph(GetExample(CurrentType));
            }
            else
            {
                text += Paragraph(GetSummary(CurrentType));
                text += Code(GetSyntax(CurrentType));
                text += Paragraph(GenerateAttributeBadges(CurrentType));
                text += Paragraph(GetRemarks(CurrentType));
                text += Paragraph(GetExample(CurrentType));

                if (CurrentType.IsEnum)
                {
                    // Generate Body for Enum
                    text += GenerateEnumBody();
                }
                else
                {
                    // Generate Body for Objects
                    text += GenerateObjectBody();
                }
            }

            return text;
        }

        protected virtual string GenerateMembersDocument(IEnumerable<MemberInfo> members)
        {
            // Validate all members are declared in the current type.
            if (members.Any(m => m.DeclaringType != CurrentType))
            {
                throw new InvalidOperationException("All members must be declared from the current type.");
            }

            var firstMember = members.First();

            var text = Header(HeaderSize.Large, Escape(GetName(CurrentAssembly)));
            text += QuoteIndent(GenerateAssemblyHeader(CurrentAssembly));
            text += Header(HeaderSize.Medium, Escape(GetName(CurrentType) + "." + GetName(firstMember) + " (" + firstMember.MemberType + ")"));

            //
            var header = Bold("Namespace") + ": " + Link(CurrentType.Namespace, GetPath(CurrentType.Assembly)) + LineBreak();
            header += Bold("Declaring Type") + ": " + GetLink(CurrentType);
            text += QuoteIndent(header) + "\n";

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
            }

            return text;
        }

        private string GenerateObjectBody()
        {
            var text = "";

            var inherits = GetInherits(CurrentType);
            if (inherits.Any())
            {
                text += Header(HeaderSize.Small, "Inherits");
                text += string.Join(", ", inherits.Select(t =>
                {
                    if (Documentation.IsLoaded(t)) { return GetLink(t); }
                    else { return Escape(GetName(t)); }
                }));
                text += "\n\n";
            }

            var constants = StaticFields.Where(f => IsConstant(f));
            if (constants.Any())
            {
                text += Header(HeaderSize.Small, "Constants");
                text += GenerateMemberList(constants);
                text += "\n";
            }

            // Emit Instance Member Summary

            if (InstanceFields.Count > 0)
            {
                text += Header(HeaderSize.Small, "Fields");
                text += GenerateMemberList(InstanceFields);
                text += "\n";
            }

            if (InstanceProperties.Count > 0)
            {
                text += Header(HeaderSize.Small, "Properties");
                text += GenerateMemberList(InstanceProperties);
                text += "\n";
            }

            if (InstanceMethods.Count > 0)
            {
                text += Header(HeaderSize.Small, "Methods");
                text += GenerateMemberList(InstanceMethods);
                text += "\n";
            }

            if (InstanceEvents.Count > 0)
            {
                text += Header(HeaderSize.Small, "Events");
                text += GenerateMemberList(InstanceEvents);
                text += "\n";
            }

            // Emit Static Member Summary

            var staticFields = StaticFields.Where(f => !IsConstant(f));
            if (staticFields.Any())
            {
                text += Header(HeaderSize.Small, "Static Fields");
                text += GenerateMemberList(staticFields) + "\n";
            }

            if (StaticProperties.Count > 0)
            {
                text += Header(HeaderSize.Small, "Static Properties");
                text += GenerateMemberList(StaticProperties) + "\n";
            }

            if (StaticMethods.Count > 0)
            {
                text += Header(HeaderSize.Small, "Static Methods");
                text += GenerateMemberList(StaticMethods) + "\n";
            }

            if (StaticEvents.Count > 0)
            {
                text += Header(HeaderSize.Small, "Static Events");
                text += GenerateMemberList(StaticEvents) + "\n";
            }

            if (Constructors.Any())
            {
                text += Header(HeaderSize.Medium, "Constructors");
                foreach (var constructor in Constructors)
                {
                    text += GenerateMethod(constructor);
                }
            }

            // Generate Member Tables
            text += GenerateMembersTables("Fields", InstanceFields, StaticFields);
            text += GenerateMembersTables("Properties", InstanceProperties, StaticProperties);
            text += GenerateMembersTables("Events", InstanceEvents, StaticEvents);
            text += GenerateMembersTables("Methods", InstanceMethods, StaticMethods);

            text = text.Trim() + "\n\n";
            return text;
        }

        private string GenerateEnumBody()
        {
            var text = "";
            text += Table(new[] { "Name", "Summary" }, Fields.Select(m => new[] { GetName(m), GetSummary(m).CollapseSpaces() }));

            text = text.Trim() + "\n\n";
            return text;
        }

        private string GenerateField(FieldInfo field)
        {
            var text = "";
            text += Header(HeaderSize.Tiny, GetName(field));
            text += Paragraph(GetSummary(field));
            text += Code(GetSyntax(field));
            text += Paragraph(GenerateAttributeBadges(field));
            text += Paragraph(GetRemarks(field));
            text += Paragraph(GetExample(field));
            return text;
        }

        private string GenerateProperty(PropertyInfo property)
        {
            var text = "";
            text += Header(HeaderSize.Small, GetName(property));
            text += Paragraph(GetSummary(property));
            text += Code(GetSyntax(property));
            text += Paragraph(GetParameterSummary(property)); // ie, returns
            text += Paragraph(GenerateAttributeBadges(property));
            text += Paragraph(GetRemarks(property));
            text += Paragraph(GetExample(property));

            return text;
        }

        private string GenerateEvent(EventInfo @event)
        {
            var text = "";
            text += Header(HeaderSize.Tiny, GetName(@event));
            text += Paragraph(GetSummary(@event));
            text += Code(GetSyntax(@event));
            text += Paragraph(GenerateAttributeBadges(@event));
            text += Paragraph($"Type: {InlineCode(GetName(@event.EventHandlerType))}");
            text += Paragraph(GetRemarks(@event));
            text += Paragraph(GetExample(@event));
            return text;
        }

        private string GenerateMethod(MethodBase method)
        {
            var text = "";
            text += Header(HeaderSize.Small, GetMethodSignature(method, true));
            text += Paragraph(GetSummary(method));
            text += Code(GetSyntax(method));
            text += Paragraph(GenerateAttributeBadges(method));
            text += Paragraph(GetParameterSummary(method));
            text += Paragraph(GetRemarks(method));
            text += Paragraph(GetExample(method));
            return text;
        }

        private string GenerateMembersTables(string title, IEnumerable<MemberInfo> instanceMembers, IEnumerable<MemberInfo> staticMembers)
        {
            var hasInstance = instanceMembers.Any();
            var hasStatic = staticMembers.Any();

            if (hasInstance || hasStatic)
            {
                var text = Header(HeaderSize.Medium, title);

                if (hasInstance)
                {
                    text += Header(HeaderSize.Tiny, "Instance");
                    text += GenerateMemberTable(instanceMembers);
                }

                if (hasStatic)
                {
                    if (hasInstance) { text += Header(HeaderSize.Tiny, "Static"); }
                    text += GenerateMemberTable(staticMembers);
                }

                text = text.Trim() + "\n\n";
                return text;
            }
            else
            {
                return string.Empty;
            }
        }

        private string GenerateMemberTable(IEnumerable<MemberInfo> members)
        {
            return members switch
            {
                // Specific members tables
                IEnumerable<FieldInfo> fields => GenerateMemberTable(fields),
                IEnumerable<EventInfo> events => GenerateMemberTable(events),
                IEnumerable<MethodInfo> methods => GenerateMemberTable(methods),
                IEnumerable<PropertyInfo> props => GenerateMemberTable(props),

                // Generic members table
                _ => Table(new[] { "Name", "Summary" }, members.Select(m => new[] {
                    GetLink(m),
                    GetSummary(m, true).Summarize()
                }))
            };
        }

        private string GenerateMemberTable(IEnumerable<FieldInfo> fields)
        {
            return Table(new[] { "Name", "Type", "Summary" }, fields.Select(f => new[] {
                GetLink(f),
                GetLink(f.FieldType),
                GetSummary(f, true).Summarize()
            }));
        }

        private string GenerateMemberTable(IEnumerable<PropertyInfo> fields)
        {
            return Table(new[] { "Name", "Type", "Summary" }, fields.Select(f => new[] {
                GetLink(f),
                GetLink(f.GetMethod?.ReturnType ?? typeof(void)),
                GetSummary(f, true).Summarize()
            }));
        }

        private string GenerateMemberTable(IEnumerable<EventInfo> events)
        {
            return Table(new[] { "Name", "Handler Type", "Summary" }, events.Select(e => new[] {
                GetLink(e),
                GetLink(e.EventHandlerType),
                GetSummary(e, true).Summarize()
            }));
        }

        private string GenerateMemberTable(IEnumerable<MethodInfo> events)
        {
            return Table(new[] { "Name", "Return Type", "Summary" }, events.Select(m => new[] {
                Link(GetMethodSignature(m, true).Summarize(25), GetPath(m)),
                GetLink(m.ReturnType),
                GetSummary(m, true).Summarize()
            }));
        }

        private string GenerateMemberList(IEnumerable<MemberInfo> members)
        {
            return string.Join(", ", members.Select(m => GetLink(m)).Distinct()) + "\n";
        }

        #endregion

        #region Render XML Elements

        protected virtual string RenderSee(XElement element, bool textOnly = false)
        {
            var key = element.Attribute("cref").Value;

            if (Documentation.TryGetType(key, out var type))
            {
                var name = GetName(type);
                if (textOnly) { return name; }
                else
                {
                    if (Documentation.IsLoaded(type))
                    {
                        return GetLink(type);
                    }
                    else
                    {
                        return InlineCode(name);
                    }
                }
            }
            else
            if (Documentation.TryGetMemberInfo(key, out var member))
            {
                //
                var name = GetName(member);
                if (member.DeclaringType != CurrentType)
                {
                    name = $"{GetName(member.DeclaringType)}.{name}";
                }

                if (textOnly) { return name; }
                else
                {
                    if (Documentation.IsLoaded(member.DeclaringType))
                    {
                        return GetLink(member);
                    }
                    else
                    {
                        return InlineCode(name);
                    }
                }
            }
            else
            {
                // Type was not known, just use key...
                if (textOnly) { return key; }
                else { return InlineCode(key); }
            }
        }

        protected virtual string RenderParamRef(XElement e, bool textOnly = false)
        {
            return InlineCode(e.Attribute("name").Value);
        }

        protected virtual string RenderTypeParamRef(XElement e, bool textOnly = false)
        {
            return InlineCode(e.Attribute("name").Value);
        }

        protected virtual string RenderPara(XElement element, bool textOnly = false)
        {
            if (element.HasElements) { return Paragraph(RenderElement(element, textOnly)); }
            else { return LineBreak(); }
        }

        protected virtual string RenderCode(XElement element, bool textOnly = false)
        {
            var text = RenderElement(element, textOnly);
            return textOnly ? text : Code(text);
        }

        protected virtual string RenderInlineCode(XElement element, bool textOnly = false)
        {
            var text = RenderElement(element, textOnly);
            return textOnly ? text : InlineCode(text);
        }

        protected string RenderElement(XElement element, bool textOnly)
        {
            var output = "";

            if (element != null)
            {
                foreach (var node in element.Nodes())
                {
                    if (node is XElement e)
                    {
                        output += (e.Name.ToString()) switch
                        {
                            "summary" => RenderElement(e, textOnly),
                            "remarks" => RenderElement(e, textOnly),
                            "typeparamref" => RenderTypeParamRef(e, textOnly),
                            "paramref" => RenderParamRef(e, textOnly),
                            "para" => RenderPara(e, textOnly),
                            "code" => RenderCode(e, textOnly),
                            "c" => RenderInlineCode(e, textOnly),
                            "see" => RenderSee(e, textOnly),

                            // default to converting XML to text
                            _ => UnknownNode(node),
                        };
                    }
                    else
                    {
                        // Gets the string representation of the node
                        var text = node.ToString();
                        output += text.CollapseSpaces().Trim();
                    }

                    output += " ";
                }
            }

            return output.Trim();

            static string UnknownNode(XNode node)
            {
                if (node is XElement e)
                {
                    Log.Warning($"Unknown Element: {e.Name}");
                }
                else
                {
                    Log.Warning($"Unknown Node: {node.NodeType}");
                }

                return node.ToString();
            }
        }

        #endregion

        #region Render Document Styles

        protected abstract string LineBreak();

        protected abstract string Paragraph(string text);

        protected abstract string QuoteIndent(string text);

        protected abstract string Italics(string text);

        protected abstract string Bold(string text);

        protected abstract string Header(HeaderSize size, string text);

        protected abstract string Code(string text, string type = "cs");

        protected abstract string InlineCode(string text);

        protected abstract string Table(string[] headers, IEnumerable<string[]> rows);

        protected abstract string Link(string text, string target);

        protected abstract string UnorderedList(IEnumerable<string> items);

        protected abstract string Badge(string text);

        protected abstract string Escape(string text);

        #endregion

        protected enum HeaderSize
        {
            Large = 1,
            Medium = 2,
            Small = 3,
            Tiny = 4
        }

        protected string GetSummary(MemberInfo method, bool textOnly = false)
        {
            var documentation = Documentation.GetDocumentation(method);
            return RenderElement(documentation?.Element("summary"), textOnly);
        }

        protected string GetRemarks(MemberInfo method, bool textOnly = false)
        {
            var documentation = Documentation.GetDocumentation(method);
            return RenderElement(documentation?.Element("remarks"), textOnly);
        }

        protected string GetExample(MemberInfo method, bool textOnly = false)
        {
            var documentation = Documentation.GetDocumentation(method);
            return RenderElement(documentation?.Element("example"), textOnly);
        }

        private string GenerateAttributeBadges(MemberInfo info)
        {
            return Paragraph(string.Join(" ", GetAttributes(info).Select(a => Badge(a))));
        }

        private IEnumerable<string> GetAttributes(MemberInfo info)
        {
            return info.GetCustomAttributes(true).Where(attr =>
            {
                // Skip attributes we know we don't want
                if (attr is IteratorStateMachineAttribute) { return false; }
                if (attr is DefaultMemberAttribute) { return false; }
                return true;
            }).Select(s => GetName(s.GetType()));
        }

        #region Member

        protected string GetName(MemberInfo member)
        {
            return member switch
            {
                MethodInfo method => GetName(method),
                ConstructorInfo constructor => GetName(constructor),
                PropertyInfo property => GetName(property),
                FieldInfo field => GetName(field),
                EventInfo @event => GetName(@event),

                // Failsafe name
                _ => member.Name,
            };
        }

        protected string GetLink(MemberInfo member)
        {
            if (Documentation.IsLoaded(member.DeclaringType)) { return Link(Escape(GetName(member)), GetPath(member)); }
            else { return InlineCode(GetName(member)); }
        }

        private bool IsVisible(MemberInfo member)
        {
            return member switch
            {
                MethodInfo method => IsVisible(method),
                ConstructorInfo constructor => IsVisible(constructor),
                PropertyInfo property => IsVisible(property),
                FieldInfo field => IsVisible(field),
                EventInfo @event => IsVisible(@event),
                _ => false,
            };
        }

        #endregion

        #region Type

        protected string GetName(Type type)
        {
            return type.GetHumanName();
        }

        protected string GetLink(Type type)
        {
            var orig = type;

            // Link to the type specified by the array
            if (type.IsArray) { type = type.GetElementType(); }
            // todo: Do the same with List<T>, IEnumerable<T>, etc (if simple enough...)

            // Determine if the type is a generic type (ie, the T of List<T>)
            var isGeneric = type.IsGenericMethodParameter
                         || type.IsGenericTypeParameter
                         || type.IsGenericParameter;

            // If the type is one of the loaded for documention and not-generic
            if (Documentation.IsLoaded(type) && !isGeneric)
            {
                // We can render the link
                return Link(Escape(GetName(orig)), GetPath(type));
            }
            else
            {
                // Visualize as inline-code instead.
                return InlineCode(GetName(orig));
            }
        }

        protected string GetSyntax(Type type)
        {
            // Get access modifiers (ie, 'public static class')
            var access = string.Join(' ', GetModifiers(type));
            access = access.CollapseSpaces();
            access = access.Trim();

            // 
            var inherits = GetInherits(type);

            // Combine access, name and inheritence list
            var text = $"{access} {type.GetHumanName()}";
            if (inherits.Count > 0) { text += $" : {string.Join(", ", inherits.Select(t => t.GetHumanName()))}"; }

            return text.Trim();
        }

        protected IReadOnlyList<Type> GetInherits(Type type)
        {
            var inherits = new List<Type>();
            if (!type.IsValueType && type.BaseType != typeof(object) && type.BaseType != null)
            {
                inherits.Add(type.BaseType);
            }

            // Append interfaces
            foreach (var interfaceType in type.GetInterfaces())
            {
                inherits.Add(interfaceType);
            }

            return inherits;
        }

        protected static IEnumerable<string> GetModifiers(Type type)
        {
            var modifiers = new List<string>();

            // Visibility modifiers
            if (type.IsPublic) { modifiers.Add("public"); }
            else if (type.IsNestedFamily || type.IsNestedFamORAssem) { modifiers.Add("protected"); }

            if (!type.IsValueType && !type.IsDelegate() && !type.IsInterface)
            {
                // Class modifiers
                if (type.IsStaticClass()) { modifiers.Add("static"); }
                else if (type.IsAbstract) { modifiers.Add("abstract"); }
                else if (type.IsSealed) { modifiers.Add("sealed"); }
            }

            // class, struct, delegate, etc
            modifiers.Add($"{GetObjectKind(type)}".ToLower());
            return modifiers;
        }

        protected enum ObjectKind
        {
            Unknown,
            Class,
            Struct,
            Interface,
            Enum,
            Delegate
        }

        protected static ObjectKind GetObjectKind(Type type)
        {
            if (type.IsDelegate()) { return ObjectKind.Delegate; }
            else if (type.IsClass) { return ObjectKind.Class; }
            else if (type.IsEnum) { return ObjectKind.Enum; }
            else if (type.IsInterface) { return ObjectKind.Interface; }
            else if (type.IsValueType) { return ObjectKind.Struct; }
            else
            {
                // todo: throw exception?
                return ObjectKind.Unknown;
            }
        }

        #endregion

        #region Assembly

        protected string GetName(Assembly assembly)
        {
            return GetName(assembly.GetName());
        }

        protected string GetName(AssemblyName assemblyName)
        {
            return assemblyName.Name;
        }

        #endregion

        #region Constructor

        protected string GetName(ConstructorInfo c)
        {
            var name = c.DeclaringType.Name;

            if (c.DeclaringType.IsGenericType)
            {
                // Strip generic grave character
                var index = name.IndexOf("`");
                if (index >= 0) { name = name.Substring(0, index); }
                return name;
            }

            return name;
        }

        #endregion

        #region Field

        private bool IsVisible(FieldInfo m)
        {
            return !m.IsSpecialName && (m.IsFamily || m.IsPublic);
        }

        private bool IsConstant(FieldInfo f)
        {
            return f.IsLiteral && !f.IsInitOnly;
        }

        private bool IsReadOnly(FieldInfo f)
        {
            return f.IsInitOnly && f.IsInitOnly;
        }

        protected string GetName(FieldInfo p)
        {
            return p.Name;
        }

        protected string GetSyntax(FieldInfo field)
        {
            var list = new List<string>();
            if (field.IsPublic) { list.Add("public"); }
            if (field.IsFamily) { list.Add("protected"); }
            if (IsConstant(field)) { list.Add("const"); }
            else if (field.IsStatic) { list.Add("static"); }
            if (IsReadOnly(field)) { list.Add("readonly"); }

            var access = string.Join(' ', list);

            var text = $"{access.Trim()} {GetName(field.FieldType)} {GetName(field)}";

            if (field.IsLiteral)
            {
                text += $" = {field.GetRawConstantValue()}";
            }

            return text.CollapseSpaces().Trim();
        }

        #endregion

        #region Property

        private bool IsVisible(PropertyInfo prop)
        {
            if (prop.CanRead) { return IsVisible(prop.GetMethod, true); }
            return false;
        }

        protected string GetName(PropertyInfo property)
        {
            if (property.GetIndexParameters().Any()) { return $"Indexer"; }
            else { return property.Name; }
        }

        protected string GetSyntax(PropertyInfo property)
        {
            var modifiers = string.Join(' ', GetModifiers(property));
            var isProtected = !modifiers.Contains("protected");

            var methods = "";
            if (property.CanRead && IsVisible(property.GetMethod, true))
            {
                // If getter is protected, but only if the property itself isn't protected
                if (property.GetMethod.IsFamily && isProtected) { methods += "protected "; }
                methods += "get; ";
            }

            if (property.CanWrite && IsVisible(property.SetMethod, true))
            {
                // If setter is protected, but only if the property itself isn't protected
                if (property.SetMethod.IsFamily && !isProtected) { methods += "protected "; }
                methods += "set;";
            }

            // Get the return type
            var returnType = GetName(property.GetMethod.ReturnType);

            // Get the index parameters
            var parameters = property.GetIndexParameters().Select(p => GetSignature(p, false));
            if (parameters.Any())
            {
                var args = string.Join(", ", parameters);
                var text = $"{modifiers} {returnType} this[{args}] {{ {methods.Trim()} }}";
                return text.CollapseSpaces();
            }
            else
            {
                var text = $"{modifiers} {returnType} {GetName(property)} {{ {methods.Trim()} }}";
                return text.CollapseSpaces();
            }
        }

        private string GetParameterSummary(PropertyInfo property)
        {
            var text = "";

            if (property.CanRead)
            {
                text += QuoteIndent(Bold("Returns") + ": " + GetLink(property.GetMethod.ReturnType));
            }

            return text;
        }

        protected IEnumerable<string> GetModifiers(PropertyInfo property)
        {
            var modifiers = new HashSet<string>();
            var methods = GetMethods();

            // Get property method modifiers
            if (methods.Any(m => m?.IsPublic ?? false)) { modifiers.Add("public"); }
            else if (methods.Any(m => m?.IsFamily ?? false)) { modifiers.Add("protected"); }
            if (methods.Any(m => m?.IsStatic ?? false)) { modifiers.Add("static"); }

            return modifiers;

            IEnumerable<MethodInfo> GetMethods()
            {
                yield return property.GetMethod;
                yield return property.SetMethod;
            }
        }

        #endregion

        #region Event

        private static bool IsVisible(EventInfo m)
        {
            return !m.IsSpecialName;
        }

        protected string GetName(EventInfo @event)
        {
            return @event.Name;
        }

        protected string GetSyntax(EventInfo @event)
        {
            var modifiers = string.Join(' ', GetModifiers(@event));
            var isProtected = !modifiers.Contains("protected");

            var methods = "";
            if (IsVisible(@event.AddMethod, true))
            {
                // If getter is protected, but only if the property itself isn't protected
                if (@event.AddMethod.IsFamily && isProtected) { methods += "protected "; }
                methods += "add; ";
            }

            if (IsVisible(@event.RemoveMethod, true))
            {
                // If setter is protected, but only if the property itself isn't protected
                if (@event.RemoveMethod.IsFamily && !isProtected) { methods += "protected "; }
                methods += "remove;";
            }

            // Get the return type
            var handlerType = GetName(@event.EventHandlerType);

            // Create output
            var output = $"{modifiers} {handlerType} {GetName(@event)} {{ {methods.Trim()} }}";
            return output.CollapseSpaces();
        }

        protected IEnumerable<string> GetModifiers(EventInfo @event)
        {
            var modifiers = new HashSet<string>();
            var methods = GetMethods();

            // Get event method modifiers
            if (methods.Any(m => m?.IsPublic ?? false)) { modifiers.Add("public"); }
            else if (methods.Any(m => m?.IsFamily ?? false)) { modifiers.Add("protected"); }
            if (methods.Any(m => m?.IsStatic ?? false)) { modifiers.Add("static"); }

            return modifiers;

            IEnumerable<MethodInfo> GetMethods()
            {
                yield return @event.AddMethod;
                yield return @event.RemoveMethod;
            }
        }

        #endregion

        #region Method

        private bool IsVisible(MethodBase m, bool isProperty = false)
        {
            var visible = (m.IsFamily || m.IsPublic) && !IsIgnoredMethodName(m.Name);
            if (!isProperty) { visible = visible && !m.IsSpecialName; }
            return visible;
        }

        protected string GetName(MethodInfo method)
        {
            if (method.IsGenericMethod)
            {
                var name = method.Name;
                var indTick = name.IndexOf("`");
                if (indTick >= 0) { name = name.Substring(0, indTick); }

                var genericTypes = method.GetGenericArguments().Select(t => t.GetHumanName());
                return $"{name}<{string.Join(", ", genericTypes)}>";
            }
            else
            {
                return $"{method.Name}";
            }
        }

        protected string GetMethodSignature(MethodBase method, bool compact)
        {
            // Create signature string (ie, 'Add(int, int)')
            var parameters = string.Join(", ", GetParameters(method, compact));
            return $"{GetName(method)}({parameters})";
        }

        protected string GetSyntax(MethodBase method)
        {
            return GetMethodSyntax(GetName(method), method);
        }

        private string GetMethodSyntax(string name, MethodBase method)
        {
            // Get the method modifiers (ie, 'protected static')
            var access = string.Join(' ', GetModifiers(method));

            // Get the return type
            var returnName = (method is MethodInfo m)
                           ? GetName(m.ReturnType)
                           : string.Empty;

            // Create syntax string (ie, 'public int Add(int x, int y)')
            var parameters = string.Join(", ", GetParameters(method, false));
            var syntax = $"{access} {returnName} {name}({parameters})";
            return syntax.CollapseSpaces();
        }

        private string GetParameterSummary(MethodBase method)
        {
            var text = "";

            // Get the XML documentation and parameters
            var documentation = Documentation.GetDocumentation(method);
            var parameters = method.GetParameters();

            // Emit parameter info
            if (parameters.Any())
            {
                text += Table(new[] { "Name", "Type", "Summary" }, parameters.Select(param =>
                {
                    var paramText = "";

                    if (documentation != null)
                    {
                        // Get parameter docs
                        var paramInfo = RenderElement(documentation.Elements("param").FirstOrDefault(e => e.Attribute("name").Value == param.Name), true);
                        paramInfo = paramInfo.Summarize();

                        if (!string.IsNullOrEmpty(paramInfo))
                        {
                            paramText += paramInfo;
                        }
                    }

                    return new[] {
                        GetName(param),
                        GetLink(param.ParameterType),
                        paramText
                    };
                }));
            }

            // Emit return info
            if (method is MethodInfo m)
            {
                var paramText = Bold("Returns") + " - " + GetLink(m.ReturnParameter.ParameterType);

                if (documentation != null)
                {
                    // Get return docs
                    var paramInfo = RenderElement(documentation?.Element("returns"), true);
                    paramInfo = paramInfo.Summarize();

                    if (!string.IsNullOrEmpty(paramInfo))
                    {
                        paramText += $" - {paramInfo}";
                    }
                }

                text += QuoteIndent($"{paramText}  \n");
            }

            return text.Trim();
        }

        protected IEnumerable<string> GetModifiers(MethodBase method)
        {
            var modifiers = new List<string>();
            if (method.IsPublic) { modifiers.Add("public"); }
            if (method.IsFamily) { modifiers.Add("protected"); }
            if (method.IsStatic) { modifiers.Add("static"); }
            if (method.IsAbstract) { modifiers.Add("abstract"); }
            return modifiers;
        }

        protected IEnumerable<string> GetParameters(MethodBase method, bool compact)
        {
            // Either 'non-compact' (T a, int b) or 'compact' (T, int)
            return method.GetParameters().Select(p => GetSignature(p, compact));
        }

        #endregion

        #region Delegate

        protected string GetDelegateSyntax(Type delegateType)
        {
            var invoke = CurrentType.GetMethod("Invoke");
            return GetMethodSyntax(GetName(delegateType), invoke);
        }

        #endregion

        #region Parameter Info

        protected string GetName(ParameterInfo parameter)
        {
            return parameter.Name;
        }

        protected string GetSignature(ParameterInfo parameter, bool compact)
        {
            var prefix = "";
            var suffix = "";

            // Is Ref
            if (parameter.ParameterType.IsByRef)
            {
                if (parameter.IsOut) { prefix += "out "; }
                else if (parameter.IsIn) { prefix += "in "; }
                else { prefix += "ref "; }
            }

            // Pointer?

            // Optional
            if (parameter.IsOptional)
            {
                var defval = parameter.DefaultValue;
                if (defval == null) { defval = "null"; }
                else if (defval is string) { defval = $"\"{defval}\""; }
                suffix += $" = {defval}";
            }

            // Params
            if (parameter.GetCustomAttribute<ParamArrayAttribute>() != null)
            {
                prefix += "params ";
            }

            // Strip & if present from parameter name
            // todo: should this be part of GetHumanName() ??
            var paramTypeName = parameter.ParameterType.GetHumanName();
            if (paramTypeName.EndsWith('&')) { paramTypeName = paramTypeName[0..^1]; }

            // Return either the compact or full parameter signature
            var signature = $"{prefix}{paramTypeName}";
            if (!compact) { signature += $" {GetName(parameter)}{suffix}"; }
            return signature.Trim();
        }

        #endregion

        #region Get Path or Directory

        protected string GetRootDirectory(AssemblyName assembly)
        {
            return $"{OutputDirectory}/{assembly.Name}";
        }

        protected string GetRootDirectory(Assembly assembly)
        {
            return GetRootDirectory(assembly.GetName());
        }

        protected string GetRootDirectory(Type type)
        {
            return GetRootDirectory(type.Assembly);
        }

        protected string GetPath(AssemblyName assemblyName)
        {
            var root = Path.GetDirectoryName(GetRootDirectory(assemblyName));

            // Get the file name for storing the type document
            var path = $"{root}/{GetName(assemblyName)}.txt";
            path = Path.ChangeExtension(path, Extension);
            return path.SanitizePath();
        }

        protected string GetPath(Assembly assembly)
        {
            var root = Path.GetDirectoryName(GetRootDirectory(assembly));

            // Get the file name for storing the type document
            var path = $"{root}/{GetName(assembly)}.txt";
            path = Path.ChangeExtension(path, Extension);
            return path.SanitizePath();
        }

        protected string GetPath(Type type)
        {
            var root = GetRootDirectory(type.Assembly);

            // Get the file name for storing the type document
            var path = $"{root}/{type.Namespace}/{type.GetHumanName()}.txt";
            path = Path.ChangeExtension(path, Extension);
            return path.SanitizePath();
        }

        protected string GetPath(MemberInfo member)
        {
            var type = member.DeclaringType;
            var root = GetRootDirectory(type.Assembly);

            // Get the file name for storing the type document
            var path = $"{root}/{type.Namespace}/{type.GetHumanName()}/{GetName(member)}.txt";
            path = Path.ChangeExtension(path, Extension);
            return path.SanitizePath();
        }

        protected string GetRelativePath(string path)
        {
            var current = Path.GetDirectoryName(CurrentPath);
            path = Path.GetRelativePath(current, path);
            return path.SanitizePath();
        }

        #endregion

        private static bool IsIgnoredMethodName(string name)
        {
            return name == "Equals"
                || name == "ToString"
                || name == "GetHashCode"
                || name == "Finalize";
        }

        protected IEnumerable<string> GetDependencies()
        {
            // Emit assembly names where internals are visible
            var references = CurrentAssembly.GetReferencedAssemblies();
            if (references.Length > 1)
            {
                var list = new List<string>();
                foreach (var referenceName in references)
                {
                    if (referenceName.Name == "netstandard") { continue; }
                    list.Add(Link(GetName(referenceName), GetPath(referenceName)));
                }

                return list;
            }
            else
            {
                return Array.Empty<string>();
            }
        }

        protected string GetFrameworkString()
        {
            var framework = CurrentAssembly.GetCustomAttribute<TargetFrameworkAttribute>();
            var displayName = framework.FrameworkDisplayName;
            if (string.IsNullOrWhiteSpace(displayName)) { displayName = framework.FrameworkName; }
            return displayName;
        }
    }
}
