using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
                InstanceFields = type.GetFields(InstanceBinding).Where(m => IsVisible(m)).ToArray();
                InstanceProperties = type.GetProperties(InstanceBinding).Where(m => IsVisible(m)).ToArray();
                InstanceMethods = type.GetMethods(InstanceBinding).Where(m => IsVisible(m)).ToArray();
                InstanceEvents = type.GetEvents(InstanceBinding).Where(m => IsVisible(m)).ToArray();

                // Get Static Members
                StaticFields = type.GetFields(StaticBinding).Where(m => IsVisible(m)).ToArray();
                StaticProperties = type.GetProperties(StaticBinding).Where(m => IsVisible(m)).ToArray();
                StaticMethods = type.GetMethods(StaticBinding).Where(m => IsVisible(m)).ToArray();
                StaticEvents = type.GetEvents(StaticBinding).Where(m => IsVisible(m)).ToArray();

                // Get Concatenated Members
                Fields = InstanceFields.Concat(StaticFields).ToArray();
                Properties = InstanceProperties.Concat(StaticProperties).ToArray();
                Methods = InstanceMethods.Concat(StaticMethods).ToArray();
                Events = InstanceEvents.Concat(StaticEvents).ToArray();

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
            header += $"{Bold("Framework")}: {GetFrameworkString()}  \n";
            header += $"{Bold("Assembly")}: {Link(GetName(assembly), GetPath(assembly))}  \n";
            return header;
        }

        protected virtual string GenerateAssemblyDocument()
        {
            var text = Header(HeaderSize.Large, Escape(GetName(CurrentAssembly)));
            text += QuoteIndent(GenerateAssemblyHeader(CurrentAssembly));
            text += "\n\n";

            var dependencies = GetDependencies();
            if (dependencies.Any())
            {
                text += Header(HeaderSize.Medium, "Assembly Dependencies");
                text += UnorderedList(dependencies);
            }

            // Generate TOC
            foreach (var namespaceGroup in Types.GroupBy(t => t.Namespace).OrderBy(g => g.Key))
            {
                text += Header(HeaderSize.Medium, $"{namespaceGroup.Key}");

                foreach (var typeGroup in namespaceGroup.GroupBy(t => GetObjectType(t)).OrderBy(g => g.Key))
                {
                    // 
                    text += Header(HeaderSize.Small, $"{typeGroup.Key}");

                    // Generate type table
                    text += Table("Name", "Summary", typeGroup.OrderBy(t => GetTypeSortKey(t))
                                                              .Select(t => (Link(GetName(t), GetPath(t)), GetSummary(t).NormalizeSpaces())));
                }
            }

            return text;

            static string GetTypeSortKey(Type type)
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
            text += QuoteIndent(GenerateAssemblyHeader(CurrentAssembly)) + "\n";

            var header = Bold("Namespace") + ": " + Link(CurrentType.Namespace, GetPath(CurrentType.Assembly)) + "  \n";
            text += Header(HeaderSize.Medium, Escape(GetName(CurrentType)));
            text += QuoteIndent(header) + "\n";

            if (CurrentType.IsDelegate())
            {
                text += GetSummary(CurrentType) + "\n\n";
                text += Code(GetDelegateSyntax(CurrentType)) + "\n\n";
                text += GetRemarks(CurrentType) + "\n\n";
                text += GetExample(CurrentType) + "\n\n";
            }
            else
            {
                text += GetSummary(CurrentType) + "\n\n";
                text += Code(GetSyntax(CurrentType)) + "\n\n";
                text += GetRemarks(CurrentType) + "\n\n";
                text += GetExample(CurrentType) + "\n\n";

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

            var text = Header(HeaderSize.Large, Escape(GetName(CurrentAssembly)));
            text += QuoteIndent(GenerateAssemblyHeader(CurrentAssembly)) + "\n";
            text += Header(HeaderSize.Medium, Escape(GetName(CurrentType) + "." + GetName(members.First())));

            //
            var header = Bold("Namespace") + ": " + Link(CurrentType.Namespace, GetPath(CurrentType.Assembly)) + "  \n";
            header += Bold("Type") + ": " + Link(GetName(CurrentType), GetPath(CurrentType)) + "  \n";
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
                    if (Documentation.IsLoaded(t)) { return Link(GetName(t), GetPath(t)); }
                    else { return Escape(GetName(t)); }
                }));
                text += "\n\n";
            }

            // Emit Instance Member Summary

            if (InstanceFields.Count > 0)
            {
                text += Header(HeaderSize.Tiny, "Fields");
                text += GenerateMemberList(InstanceFields);
                text += "\n";
            }

            if (InstanceProperties.Count > 0)
            {
                text += Header(HeaderSize.Tiny, "Properties");
                text += GenerateMemberList(InstanceProperties);
                text += "\n";
            }

            if (InstanceMethods.Count > 0)
            {
                text += Header(HeaderSize.Tiny, "Methods");
                text += GenerateMemberList(InstanceMethods);
                text += "\n";
            }

            if (InstanceEvents.Count > 0)
            {
                text += Header(HeaderSize.Tiny, "Events");
                text += GenerateMemberList(InstanceEvents);
                text += "\n";
            }

            // Emit Static Member Summary

            if (StaticFields.Count > 0)
            {
                text += Header(HeaderSize.Tiny, "Static Fields");
                text += GenerateMemberList(StaticFields);
                text += "\n";
            }

            if (StaticProperties.Count > 0)
            {
                text += Header(HeaderSize.Tiny, "Static Properties");
                text += GenerateMemberList(StaticProperties);
                text += "\n";
            }

            if (StaticMethods.Count > 0)
            {
                text += Header(HeaderSize.Tiny, "Static Methods");
                text += GenerateMemberList(StaticMethods);
                text += "\n";
            }

            if (StaticEvents.Count > 0)
            {
                text += Header(HeaderSize.Tiny, "Static Events");
                text += GenerateMemberList(StaticEvents);
                text += "\n";
            }

            if (Constructors.Any())
            {
                text += Header(HeaderSize.Medium, "Constructors");
                foreach (var constructor in Constructors)
                {
                    text += GenerateMethod(constructor);
                }
            }

            if (Fields.Any())
            {
                text += Header(HeaderSize.Medium, "Fields");
                text += GenerateMemberTable(Fields);
                text = text.Trim() + "\n\n";
            }

            if (Properties.Any())
            {
                text += Header(HeaderSize.Medium, "Properties");
                text += GenerateMemberTable(Properties);
                text = text.Trim() + "\n\n";
            }

            if (Events.Any())
            {
                text += Header(HeaderSize.Medium, "Events");
                text += GenerateMemberTable(Events);
                text = text.Trim() + "\n\n";
            }

            if (Methods.Any())
            {
                text += Header(HeaderSize.Medium, "Methods");
                text += GenerateMemberTable(Methods);
                text = text.Trim() + "\n\n";
            }

            text = text.Trim() + "\n\n";
            return text;
        }

        private string GenerateEnumBody()
        {
            var text = "";
            text += Table("Name", "Summary", Fields.Select(m => (GetName(m), GetSummary(m).NormalizeSpaces())));
            return text.Trim() + "\n";
        }

        private string GenerateField(FieldInfo field)
        {
            var text = "";
            text += Header(HeaderSize.Tiny, GetName(field));
            text += GetSummary(field) + "\n\n";
            text += Code(GetSyntax(field)) + "\n\n";
            text += GetRemarks(field) + "\n\n";
            text += GetExample(field) + "\n\n";
            return text;
        }

        private string GenerateProperty(PropertyInfo property)
        {
            var text = "";
            text += Header(HeaderSize.Small, GetName(property));
            text += GetSummary(property) + "\n\n";
            text += Code(GetSyntax(property)) + "\n\n";
            text += GetRemarks(property) + "\n\n";
            text += GetExample(property) + "\n\n";
            return text;
        }

        private string GenerateEvent(EventInfo @event)
        {
            var text = "";
            text += Header(HeaderSize.Tiny, GetName(@event));
            text += GetSummary(@event) + "\n\n";
            text += $"Type: {InlineCode(GetName(@event.EventHandlerType))}\n\n";
            text += GetRemarks(@event) + "\n\n";
            text += GetExample(@event) + "\n\n";
            return text;
        }

        private string GenerateMethod(MethodBase method)
        {
            var text = "";
            text += Header(HeaderSize.Small, GetMethodSignature(method, true));
            text += GetSummary(method) + "\n\n";
            text += Code(GetMethodSyntax(method));
            // todo: method paramters
            text += GetRemarks(method) + "\n\n";
            text += GetExample(method) + "\n\n";
            return text;
        }

        private string GenerateMemberTable(IEnumerable<MemberInfo> members)
        {
            return Table("Name", "Summary", members.Select(f => (Link(GetName(f), GetPath(f)), Escape(GetSummary(f).NormalizeSpaces()))));
        }

        private string GenerateMemberList(IEnumerable<MemberInfo> members)
        {
            return string.Join(", ", members.Select(d => Link(GetName(d), GetPath(d))).Distinct()) + "\n";
        }

        #endregion

        #region Render XML Elements

        protected virtual string RenderSee(XElement element)
        {
            var key = element.Attribute("cref").Value;

            if (Documentation.TryGetType(key, out var type))
            {
                var name = GetName(type);
                if (Documentation.IsLoaded(type)) { return Link(name, GetPath(type)); }
                else { return InlineCode(name); }
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

                if (Documentation.IsLoaded(member.DeclaringType)) { return Link(name, GetPath(member)); }
                else { return InlineCode(name); }
            }
            else
            {
                // Type was not known, just use key...
                return InlineCode(key);
            }
        }

        protected virtual string RenderParamRef(XElement e)
        {
            return InlineCode(e.Attribute("name").Value);
        }

        protected virtual string RenderPara(XElement element)
        {
            return $"{RenderElement(element)}\n";
        }

        protected virtual string RenderCode(XElement element)
        {
            return Code(RenderElement(element));
        }

        protected string RenderElement(XElement element)
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
                            "summary" => RenderElement(e),
                            "remarks" => RenderElement(e),
                            "paramref" => RenderParamRef(e),
                            "para" => RenderPara(e),
                            "code" => RenderCode(e),
                            "see" => RenderSee(e),

                            // default to converting XML to text
                            _ => node.ToString(),
                        };
                    }
                    else
                    {
                        // Gets the string representation of the node
                        var text = node.ToString();
                        output += text.NormalizeSpaces().Trim();
                    }

                    output += " ";
                }
            }

            return output.Trim();
        }

        #endregion

        #region Render Document Styles

        protected abstract string Preformatted(string text);

        protected abstract string QuoteIndent(string text);

        protected abstract string Italics(string text);

        protected abstract string Bold(string text);

        protected abstract string Header(HeaderSize size, string text);

        protected abstract string Code(string text, string type = "cs");

        protected abstract string InlineCode(string text);

        protected abstract string Table(string headerLeft, string headerRight, IEnumerable<(string left, string right)> rows);

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

        protected string GetSummary(MemberInfo method)
        {
            var documentation = Documentation.GetDocumentation(method);
            return RenderElement(documentation?.Element("summary"));
        }

        protected string GetRemarks(MemberInfo method)
        {
            var documentation = Documentation.GetDocumentation(method);
            return RenderElement(documentation?.Element("remarks"));
        }

        protected string GetExample(MemberInfo method)
        {
            var documentation = Documentation.GetDocumentation(method);
            return RenderElement(documentation?.Element("example"));
        }

        #region Badges

        private string GenerateBadges(Type type)
        {
            var badges = new List<string>();
            badges.AddRange(GetTypeBadges(type));
            return GetBadgeText(badges);
        }

        private string GenerateBadges(ConstructorInfo constructor)
        {
            var badges = new List<string>();
            badges.AddRange(GetMemberBadges(constructor));
            return GetBadgeText(badges);
        }

        private string GenerateBadges(PropertyInfo property, bool isStatic)
        {
            var badges = new List<string>();

            // Emit Static Badge
            if (isStatic) { badges.Add("Static"); }

            // Emit Get/Set Badges
            var canRead = property.CanRead && (property.GetMethod.IsPublic || property.GetMethod.IsFamily);
            var canWrite = property.CanWrite && (property.SetMethod.IsPublic || property.SetMethod.IsFamily);
            if (!canRead || !canWrite)
            {
                if (canRead) { badges.Add("Read Only"); }
                if (canWrite) { badges.Add("Write Only"); }
            }

            // 
            badges.AddRange(GetMemberBadges(property));
            return GetBadgeText(badges);
        }

        private string GenerateBadges(FieldInfo field, bool isStatic)
        {
            var badges = new List<string>();

            // Emit Static Badge
            if (isStatic) { badges.Add("Static"); }

            // Emit Real Only
            if (field.IsInitOnly) { badges.Add("Read Only"); }

            // 
            badges.AddRange(GetMemberBadges(field));
            return GetBadgeText(badges);
        }

        private string GenerateBadges(MethodInfo method, bool isStatic)
        {
            var badges = new List<string>();

            // Emit Static Badge
            if (isStatic) { badges.Add("Static"); }

            // 
            if (method.IsAbstract) { badges.Add("Abstract"); }
            else if (method.IsVirtual) { badges.Add("Virtual"); }
            if (method.IsFamily) { badges.Add("Protected"); }

            // 
            badges.AddRange(GetMemberBadges(method));
            return GetBadgeText(badges);
        }

        private IEnumerable<string> GetMemberBadges(MemberInfo info)
        {
            return info.GetCustomAttributes(true)
                       .Select(s => GetName(s.GetType()));
        }

        private IEnumerable<string> GetTypeBadges(Type type)
        {
            return type.GetCustomAttributes(true)
                       .Select(s => GetName(s.GetType()));
        }

        private string GetBadgeText(IEnumerable<string> tokens)
        {
            return tokens.Any()
                ? $"<small>{string.Join(", ", tokens.Select(s => Badge(s))).Trim()}</small>\n"
                : string.Empty;
        }

        #endregion

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

        #region Type

        protected static string GetName(Type type)
        {
            return type.GetHumanName();
        }

        protected static string GetSyntax(Type type)
        {
            // Get access modifiers (ie, 'public static class')
            var access = string.Join(' ', GetModifiers(type));
            access = access.NormalizeSpaces();
            access = access.Trim();

            // 
            var inherits = GetInherits(type);

            // Combine access, name and inheritence list
            var text = $"{access} {type.GetHumanName()}";
            if (inherits.Count > 0) { text += $" : {string.Join(", ", inherits.Select(t => t.GetHumanName()))}"; }
            return text.NormalizeSpaces().Trim();
        }

        protected static IReadOnlyList<Type> GetInherits(Type type)
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
            else if (type.IsNestedFamily) { modifiers.Add("protected"); }

            if (!type.IsValueType && !type.IsDelegate())
            {
                // Class modifiers
                if (type.IsStaticClass()) { modifiers.Add("static"); }
                else if (type.IsAbstract) { modifiers.Add("abstract"); }
                else if (type.IsSealed) { modifiers.Add("sealed"); }
            }

            // class, struct, delegate, etc
            modifiers.Add($"{GetObjectType(type)}".ToLower());
            return modifiers;
        }

        protected enum ObjectType
        {
            Unknown,
            Class,
            Interface,
            Struct,
            Delegate,
            Enum
        }

        protected static ObjectType GetObjectType(Type type)
        {
            if (type.IsDelegate()) { return ObjectType.Delegate; }
            else if (type.IsClass) { return ObjectType.Class; }
            else if (type.IsEnum) { return ObjectType.Enum; }
            else if (type.IsInterface) { return ObjectType.Interface; }
            else if (type.IsValueType) { return ObjectType.Struct; }
            else
            {
                // todo: throw exception?
                return ObjectType.Unknown;
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

        private static bool IsVisible(FieldInfo m)
        {
            return !m.IsSpecialName && (m.IsFamily || m.IsPublic);
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
            if (field.IsStatic) { list.Add("static"); }

            var access = string.Join(' ', list);

            var text = $"{access.Trim()} {GetName(field.FieldType)} {GetName(field)}";
            return text.NormalizeSpaces().Trim();
        }

        #endregion

        #region Property

        private bool IsVisible(PropertyInfo prop)
        {
            if (prop.CanRead) { return IsVisible(prop.GetMethod, true); }
            return false;
        }

        protected string GetName(PropertyInfo p)
        {
            return p.Name;
        }

        protected string GetSyntax(PropertyInfo p)
        {
            var e = "";

            if (p.CanRead && IsVisible(p.GetMethod, true))
            {
                if (p.GetMethod.IsFamily) { e += "protected "; }
                e += "get; ";
            }

            if (p.CanWrite && IsVisible(p.SetMethod, true))
            {
                if (p.SetMethod.IsFamily) { e += "protected "; }
                e += "set;";
            }

            var ret = GetName(p.GetMethod.ReturnType);
            return $"{ret} {GetName(p)} {{ {e.Trim()} }}";
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
                return $"{name}<{string.Join("|", genericTypes)}>";
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

        protected string GetMethodSyntax(MethodBase method)
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
            return syntax.NormalizeSpaces();
        }

        protected IEnumerable<string> GetModifiers(MethodBase method)
        {
            var modifiers = new List<string>();
            if (method.IsPublic) { modifiers.Add("public"); }
            if (method.IsFamily) { modifiers.Add("protected"); }
            if (method.IsStatic) { modifiers.Add("static"); }
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
            return signature;
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
            var path = $"{root}/{type.Namespace}.{type.GetHumanName()}.txt";
            path = Path.ChangeExtension(path, Extension);
            return path.SanitizePath();
        }

        protected string GetPath(MemberInfo member)
        {
            var type = member.DeclaringType;
            var root = GetRootDirectory(type.Assembly);

            // Get the file name for storing the type document
            var path = $"{root}/{type.Namespace}.{type.GetHumanName()}.{GetName(member)}.txt";
            path = Path.ChangeExtension(path, Extension);
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
