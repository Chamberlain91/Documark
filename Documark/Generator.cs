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

        public void SetCurrentType(Type type)
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

        internal void Generate(Assembly assembly)
        {
            // Delete assembly root directory (if exists) and regenerate it
            var root = GetRootDirectory(assembly);
            if (Directory.Exists(root)) { Directory.Delete(root, true); }

            // Set the current assembly
            Types = Documentation.GetVisibleTypes(assembly).ToArray();
            CurrentAssembly = assembly;

            // Generate and write assembly file to disk
            CurrentPath = GetPath(assembly);
            Directory.CreateDirectory(Path.GetDirectoryName(CurrentPath));
            File.WriteAllText(CurrentPath, GenerateAssemblyDocument());

            // We will generate document for each type
            foreach (var type in Types)
            {
                // Sets the current type and gathers type information
                SetCurrentType(type);

                // Generate and write file to disk
                CurrentPath = GetPath(type);
                Directory.CreateDirectory(Path.GetDirectoryName(CurrentPath));
                File.WriteAllText(CurrentPath, GenerateTypeDocument());

                // Only bother with writing member files with class/structs
                if (!type.IsEnum && !type.IsDelegate())
                {
                    foreach (var memberGroup in Members.GroupBy(m => GetName(m)))
                    {
                        var member = memberGroup.First();

                        // Generate and write file to disk
                        CurrentPath = GetPath(member);
                        Directory.CreateDirectory(Path.GetDirectoryName(CurrentPath));
                        File.WriteAllText(CurrentPath, GenerateMembersDocument(memberGroup));
                    }
                }
            }
        }

        protected abstract string GenerateAssemblyDocument();

        protected abstract string GenerateTypeDocument();

        protected abstract string GenerateMembersDocument(IEnumerable<MemberInfo> members);

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

        protected abstract string Block(string text);

        protected abstract string Italics(string text, string type = "cs");

        protected abstract string Bold(string text, string type = "cs");

        protected abstract string InlineCode(string text, string type = "cs");

        protected abstract string Table(string headerLeft, string headerRight, IEnumerable<(string left, string right)> rows);

        protected abstract string Header(int size, string text);

        protected abstract string Code(string text, string type = "cs");

        protected abstract string Link(string text, string target);

        protected abstract string Badge(string text);

        protected abstract string Divider();

        protected abstract string Escape(string text);

        #endregion

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

        protected string GetName(Type type)
        {
            return type.GetHumanName();
        }

        protected string GetSyntax(Type type)
        {
            // Get access modifiers (ie, 'public static class')
            var access = $"{type.GetAccessModifiers()} {type.GetModifiers()} {$"{GetTypeType(type)}".ToLower()}";
            access = access.NormalizeSpaces();

            // 
            var inherits = GetInherits(type);

            // Combine access, name and inheritence list
            var text = $"{access.Trim()} {type.GetHumanName()}";
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

        protected enum TypeType
        {
            Unknown,
            Class,
            Interface,
            Struct,
            Delegate,
            Enum
        }

        protected static TypeType GetTypeType(Type type)
        {
            if (type.IsDelegate()) { return TypeType.Delegate; }
            else if (type.IsClass) { return TypeType.Class; }
            else if (type.IsEnum) { return TypeType.Enum; }
            else if (type.IsInterface) { return TypeType.Interface; }
            else if (type.IsValueType) { return TypeType.Struct; }
            else
            {
                // todo: throw exception?
                return TypeType.Unknown;
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

        protected string GetParameterSignature(ConstructorInfo constructor)
        {
            var parameters = constructor.GetParameters();
            return $"({string.Join(", ", parameters.Select(param => $"{GetSignature(param)}")).Trim()})";
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

        private static bool IsVisible(PropertyInfo prop)
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

        private static bool IsVisible(MethodBase m, bool property = false)
        {
            var visible = (m.IsFamily || m.IsPublic) && !IsIgnoredMethodName(m.Name);
            if (!property) { visible = visible && !m.IsSpecialName; }
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

        protected string GetSyntax(MethodBase methodBase)
        {
            var pre = new List<string>();
            if (methodBase.IsPublic) { pre.Add("public"); }
            if (methodBase.IsFamily) { pre.Add("protected"); }

            var ret = "";
            if (methodBase is MethodInfo method)
            {
                ret = GetName(method.ReturnType);
            }

            return (string.Join(' ', pre) + " " + ret + " " + GetSignature(methodBase, false)).NormalizeSpaces();
        }

        protected string GetSignature(MethodBase method, bool compact = false)
        {
            // MethodName<T>(T a, int b) or // MethodName<T>(T, int)
            var parameters = $"({string.Join(", ", method.GetParameters().Select(p => $"{GetSignature(p, compact)}")).Trim()})";
            return $"{GetName(method)}{parameters}";
        }

        #endregion

        #region Parameter Info

        protected string GetName(ParameterInfo p)
        {
            return p.Name;
        }

        protected string GetSignature(ParameterInfo p, bool compact = false)
        {
            var pre = "";
            var pos = "";

            // Is Ref
            if (p.ParameterType.IsByRef)
            {
                if (p.IsOut) { pre += "out "; }
                else if (p.IsIn) { pre += "in "; }
                else { pre += "ref "; }
            }

            // Pointer?

            // Optional
            if (p.IsOptional)
            {
                var defval = p.DefaultValue;
                if (defval == null) { defval = "null"; }
                else if (defval is string) { defval = $"\"{defval}\""; }
                pos += $" = {defval}";
            }

            // Params
            if (p.GetCustomAttribute<ParamArrayAttribute>() != null)
            {
                pre += "params ";
            }

            // 
            var paramTypeName = p.ParameterType.GetHumanName();
            if (paramTypeName.EndsWith('&')) { paramTypeName = paramTypeName[0..^1]; }

            if (compact) { return $"{pre}{paramTypeName}"; }
            else { return $"{pre}{paramTypeName} {GetName(p)}{pos}"; }
        }

        #endregion

        #region Get Path or Directory

        public string GetRootDirectory(AssemblyName assembly)
        {
            return $"{OutputDirectory}/{assembly.Name}";
        }

        public string GetRootDirectory(Assembly assembly)
        {
            return GetRootDirectory(assembly.GetName());
        }

        public string GetRootDirectory(Type type)
        {
            return GetRootDirectory(type.Assembly);
        }

        public string GetPath(AssemblyName assemblyName)
        {
            var root = Path.GetDirectoryName(GetRootDirectory(assemblyName));

            // Get the file name for storing the type document
            var path = $"{root}/{GetName(assemblyName)}.txt";
            path = Path.ChangeExtension(path, Extension);
            return path.SanitizePath();
        }

        public string GetPath(Assembly assembly)
        {
            var root = Path.GetDirectoryName(GetRootDirectory(assembly));

            // Get the file name for storing the type document
            var path = $"{root}/{GetName(assembly)}.txt";
            path = Path.ChangeExtension(path, Extension);
            return path.SanitizePath();
        }

        public string GetPath(Type type)
        {
            var root = GetRootDirectory(type.Assembly);

            // Get the file name for storing the type document
            var path = $"{root}/{type.Namespace}.{type.GetHumanName()}.txt";
            path = Path.ChangeExtension(path, Extension);
            return path.SanitizePath();
        }

        public string GetPath(MemberInfo member)
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

        public IEnumerable<string> GetDependencies()
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

        public string GetFrameworkString()
        {
            var framework = CurrentAssembly.GetCustomAttribute<TargetFrameworkAttribute>();
            var displayName = framework.FrameworkDisplayName;
            if (string.IsNullOrWhiteSpace(displayName)) { displayName = framework.FrameworkName; }
            return displayName;
        }
    }
}
