using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;

namespace Documark
{
    public static class Documentation
    // inspired by: https://github.com/ZacharyPatten/Towel/blob/master/Sources/Towel/Meta.cs 
    {
        // key to xml documentation
        private static readonly Dictionary<string, XElement> _documentation = new Dictionary<string, XElement>();
        private static readonly HashSet<Assembly> _assemblies = new HashSet<Assembly>();

        // key to type/member
        private static readonly Dictionary<string, MemberInfo> _members = new Dictionary<string, MemberInfo>();
        private static readonly Dictionary<string, Type> _types = new Dictionary<string, Type>();

        static Documentation()
        {
            // Populate types of already assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // LoadDocumentation(assembly);
                PopulateTypes(GetVisibleTypes(assembly));
            }
        }

        #region Get Documentation

        /// <summary>
        /// Gets loaded documentation by the key encoded in the XML documentation.
        /// </summary>
        public static XElement GetDocumentation(string key)
        {
            _documentation.TryGetValue(key, out var documentation);
            return documentation;
        }

        /// <summary>
        /// Gets the XML documentation for the specified type.
        /// </summary>
        public static XElement GetDocumentation(Type type)
        {
            LoadDocumentation(type.Assembly);
            return GetDocumentation(GetKey(type));
        }

        /// <summary>
        /// Gets the XML documentation for the specified member.
        /// </summary>
        public static XElement GetDocumentation(MemberInfo member)
        {
            return member switch
            {
                FieldInfo fieldInfo => GetDocumentation(fieldInfo),
                EventInfo eventInfo => GetDocumentation(eventInfo),
                PropertyInfo propertyInfo => GetDocumentation(propertyInfo),
                MethodInfo methodInfo => GetDocumentation(methodInfo),
                ConstructorInfo constructorInfo => GetDocumentation(constructorInfo),
                Type type => GetDocumentation(type),

                // Should not happen, but to be thorough
                _ => throw new InvalidOperationException($"{nameof(GetDocumentation)} encountered an unhandled type [{member.GetType().FullName}]."),
            };
        }

        /// <summary>
        /// Gets the XML documentation for the specified method.
        /// </summary>
        private static XElement GetDocumentation(MethodInfo method)
        {
            LoadDocumentation(method.DeclaringType.Assembly);
            return GetDocumentation(GetKey(method));
        }

        /// <summary>
        /// Gets the XML documentation for the specified constructor method.
        /// </summary>
        private static XElement GetDocumentation(ConstructorInfo constructor)
        {
            LoadDocumentation(constructor.DeclaringType.Assembly);
            return GetDocumentation(GetKey(constructor));
        }

        /// <summary>
        /// Gets the XML documentation for the specified property.
        /// </summary>
        private static XElement GetDocumentation(PropertyInfo property)
        {
            LoadDocumentation(property.DeclaringType.Assembly);
            return GetDocumentation(GetKey(property));
        }

        /// <summary>
        /// Gets the XML documentation for the specified field.
        /// </summary>
        private static XElement GetDocumentation(FieldInfo field)
        {
            LoadDocumentation(field.DeclaringType.Assembly);
            return GetDocumentation(GetKey(field));
        }

        /// <summary>
        /// Gets the XML documentation for the specified event.
        /// </summary>
        private static XElement GetDocumentation(EventInfo @event)
        {
            LoadDocumentation(@event.DeclaringType.Assembly);
            return GetDocumentation($"E:{GetKey(@event)}");
        }

        /// <summary>
        /// Gets the XML documentation for the specified parameter.
        /// </summary>
        public static XElement GetDocumentation(ParameterInfo parameter)
        {
            var member = GetDocumentation(parameter.Member);

            if (member != null)
            {
                return member.Descendants("param")
                             .Where(e => e.Attribute("name").Value == parameter.Name)
                             .FirstOrDefault();
            }

            // No known documentation for parent member
            return null;
        }

        #endregion

        #region Reflection to XML Key

        public static string GetKey(MemberInfo member)
        {
            return member switch
            {
                FieldInfo fieldInfo => GetKey(fieldInfo),
                EventInfo eventInfo => GetKey(eventInfo),
                PropertyInfo propertyInfo => GetKey(propertyInfo),
                MethodInfo methodInfo => GetKey(methodInfo),
                ConstructorInfo constructorInfo => GetKey(constructorInfo),
                Type type => GetKey(type),

                // Should not happen, but to be thorough
                _ => throw new InvalidOperationException($"{nameof(GetDocumentation)} encountered an unhandled type [{member.GetType().FullName}]."),
            };
        }

        public static string GetKey(Type type)
        {
            return $"T:{GetTypeKey(type, false)}";
        }

        public static string GetKey(ConstructorInfo constructor)
        {
            return $"M:{GetMethodBaseKey(constructor)}";
        }

        public static string GetKey(MethodInfo method)
        {
            return $"M:{GetMethodBaseKey(method)}";
        }

        public static string GetKey(FieldInfo field)
        {
            return $"F:{GetMemberBaseKey(field)}";
        }

        public static string GetKey(PropertyInfo property)
        {
            return $"P:{GetMemberBaseKey(property)}";
        }

        public static string GetKey(EventInfo @event)
        {
            return GetMemberBaseKey(@event);
        }

        private static string GetMemberBaseKey(MemberInfo member)
        {
            return $"{GetTypeKey(member.DeclaringType, false)}.{member.Name}";
        }

        private static string GetMethodBaseKey(MethodBase method)
        {
            var generic = "";

            if (method.IsGenericMethod)
            {
                var genericArguments = method.GetGenericArguments();
                generic += "``" + genericArguments.Length;
            }

            // 
            var parameters = string.Join(',', method.GetParameters().Select(p => GetTypeKey(p.ParameterType, true)));
            if (!string.IsNullOrWhiteSpace(parameters)) { parameters = $"({parameters})"; }

            // 
            var name = method.Name;
            if (method is ConstructorInfo) { name = "#ctor"; }

            // 
            return $"{GetTypeKey(method.DeclaringType, false)}.{name}{generic}{parameters}";
        }

        private static string GetTypeKey(Type type, bool isParameterType)
        {
            // array, pointer or reference
            if (type.HasElementType)
            {
                // 
                if (type.IsByRef)
                {
                    // 
                    type = type.GetElementType();
                    return $"{GetTypeKey(type, isParameterType)}@";
                }
                else
                {
                    // 
                    type = type.GetElementType();
                    return GetTypeKey(type, isParameterType);
                }
            }
            else
            if (type.IsGenericType)
            {
                // Store by direct type string
                var key = type.ToString();

                var ixGrave = key.IndexOf('`');
                if (isParameterType)
                {
                    // Trim generic meta data
                    key = key.Substring(0, ixGrave);
                    key += "{" + string.Join(',', type.GenericTypeArguments.Select(t => GetTypeKey(t, false))) + "}";
                    return key;
                }
                else
                {
                    // Trim generic meta name data
                    var ixBrace = key.IndexOf('[', ixGrave);
                    return key.Substring(0, ixBrace);
                }
            }
            else
            if (type.IsGenericMethodParameter)
            {
                return $"``{type.GenericParameterPosition}";
            }
            else
            if (type.IsGenericTypeParameter)
            {
                return $"`{type.GenericParameterPosition}";
            }
            else
            {
                // Simplest Case...?
                return type.FullName;
            }
        }

        #endregion

        public static IEnumerable<Type> GetVisibleTypes(Assembly assembly)
        {
            // Filters to visible (ie, public or protected) types.
            return assembly.DefinedTypes.Where(t => t.IsPublic || t.IsNestedFamORAssem);
        }

        public static IEnumerable<MemberInfo> GetVisibleMembers(Type type)
        {
            // Filters to visible (ie, public or protected) memberts.
            return type.GetMembers(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public);
        }

        /// <summary>
        /// Using the specified XML key, try to find the associated type.
        /// </summary>
        public static bool TryGetType(string key, out Type type)
        {
            if (_types.TryGetValue(key, out type))
            {
                return true;
            }

            // Unable to find
            type = default;
            return false;
        }

        /// <summary>
        /// Using the specified XML key, try to find the associated member info.
        /// </summary>
        public static bool TryGetMemberInfo(string key, out MemberInfo info)
        {
            if (_members.TryGetValue(key, out info))
            {
                return true;
            }

            // Unable to find
            info = default;
            return false;
        }

        internal static void LoadDocumentation(Assembly assembly)
        {
            if (_assemblies.Contains(assembly))
            {
                return;
            }

            // Populate Types
            PopulateTypes(GetVisibleTypes(assembly));

            // Get assembly directory and related .xml
            var dirPath = Path.GetDirectoryName(assembly.Location);
            var xmlPath = Path.Combine(dirPath, assembly.GetName().Name + ".xml");

            // Does the XML exist?
            if (File.Exists(xmlPath))
            {
                // Load XML Document
                var xml = XDocument.Parse(File.ReadAllText(xmlPath));

                // Get child of the members node
                foreach (var member in xml.Descendants("member"))
                {
                    var name = member.Attribute("name").Value;
                    _documentation[name] = member;
                }
            }

            // Mark assembly as visited
            _assemblies.Add(assembly);
        }

        private static void PopulateTypes(IEnumerable<Type> types)
        {
            foreach (var type in types)
            {
                var key = GetKey(type);

                // Store type by key and toString()
                _types[type.ToString()] = type;
                _types[key] = type;

                // 
                foreach (var member in GetVisibleMembers(type))
                {
                    PopulateMemberInfo(member);
                }
            }
        }

        private static void PopulateMemberInfo(MemberInfo member)
        {
            // Skip these as types are handled separately
            if (member.MemberType == MemberTypes.NestedType) { return; }
            if (member.MemberType == MemberTypes.TypeInfo) { return; }

            switch (member)
            {
                case MethodInfo method:
                {
                    var key = GetKey(method);
                    _members[key] = method;
                    break;
                }

                case ConstructorInfo constructor:
                {
                    var key = GetKey(constructor);
                    _members[key] = constructor;
                    break;
                }

                case PropertyInfo property:
                {
                    var key = GetKey(property);
                    _members[key] = property;
                    break;
                }

                case FieldInfo field:
                {
                    var key = GetKey(field);
                    _members[key] = field;
                    break;
                }

                case EventInfo @event:
                {
                    var key = GetKey(@event);
                    _members[key] = @event;
                    break;
                }

                default:
                    throw new InvalidOperationException("Unable to populate member info, unknown type. " + member.MemberType);
            }
        }
    }
}
