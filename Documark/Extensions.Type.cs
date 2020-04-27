using System;
using System.Linq;

namespace Documark
{
    public static class TypeExtensions
    {
        public static bool IsStaticClass(this Type type)
        {
            return type.IsAbstract && type.IsSealed;
        }

        public static bool IsDelegate(this Type type)
        {
            return type.IsSubclassOf(typeof(Delegate));
        }

        public static string GetVariableName(this Type type)
        {
            // Pointer or Reference
            if (type.IsByRef)
            {
                return $"{GetHumanName(type.GetElementType())}Ref";
            }
            else
            // Array of Type
            if (type.IsArray)
            {
                return $"{GetHumanName(type.GetElementType())}Array";
            }
            else
            // Generic Type
            if (type.IsGenericType)
            {
                var name = type.Name;

                // Strip generic grave character
                var index = name.IndexOf("`");
                if (index >= 0) { name = name.Substring(0, index); }

                return GetName(type, name);
            }
            // Simple Type
            else
            {
                return GetName(type, type.Name);
            }

            static string GetName(Type type, string name)
            {
                if (type.IsInterface)
                {
                    // Interface naming pattern
                    if (name[0] == 'I' && char.IsUpper(name[1]))
                    {
                        name = name.Substring(1);
                    }
                }

                return name.ToSnakeCase();
            }
        }

        public static string GetHumanName(this Type type)
        {
            // Primitive Types
            if (type == typeof(bool)) { return "bool"; }
            else if (type == typeof(int)) { return "int"; }
            else if (type == typeof(uint)) { return "uint"; }
            else if (type == typeof(short)) { return "short"; }
            else if (type == typeof(ushort)) { return "ushort"; }
            else if (type == typeof(byte)) { return " byte"; }
            else if (type == typeof(sbyte)) { return "sbyte"; }
            else if (type == typeof(long)) { return "long"; }
            else if (type == typeof(ulong)) { return "ulong"; }
            else if (type == typeof(float)) { return "float"; }
            else if (type == typeof(double)) { return "double"; }
            else if (type == typeof(decimal)) { return "decimal"; }
            else if (type == typeof(char)) { return "char"; }
            else if (type == typeof(string)) { return "string"; }
            else if (type == typeof(object)) { return "object"; }
            else if (type == typeof(void)) { return "void"; }

            // Get the "A" of "A.B" types
            var pre = "";
            if (type.IsNested && !type.IsGenericParameter && !type.IsGenericTypeParameter && !type.IsGenericMethodParameter)
            {
                pre += GetHumanName(type.DeclaringType);
                pre += ".";
            }

            // Pointer or Reference
            if (type.IsByRef)
            {
                return GetHumanName(type.GetElementType());
            }
            else
            // Array of Type
            if (type.IsArray)
            {
                var c = new string(',', type.GetArrayRank() - 1);
                return $"{pre}{GetHumanName(type.GetElementType())}[{c}]";
            }
            else
            // Generic Type
            if (type.IsGenericType)
            {
                var name = type.Name;

                // Strip generic grave character
                var index = name.IndexOf("`");
                if (index >= 0) { name = name.Substring(0, index); }

                if (type.IsConstructedGenericType)
                {
                    var genericTypes = type.GenericTypeArguments.Select(t => GetHumanName(t));
                    return $"{pre}{name}<{string.Join(", ", genericTypes)}>";
                }
                else
                {
                    var genericArgs = type.GetGenericArguments().Select(t => GetHumanName(t));
                    return $"{pre}{name}<{string.Join(", ", genericArgs)}>";
                }
            }
            // Simple Type
            else
            {
                return $"{pre}{type.Name}";
            }
        }

        public static string GetAccessModifiers(this Type type)
        {
            if (type.IsPublic) { return "public"; }
            else if (type.IsNestedFamily || type.IsNestedFamORAssem) { return "protected"; }
            else
            {
                // todo: throw exception?
                return "unknown";
            }
        }

        public static string GetModifiers(this Type type)
        {
            if (!type.IsValueType && !type.IsDelegate())
            {
                if (type.IsStaticClass()) { return "static"; }
                else if (type.IsAbstract) { return "abstract"; }
                else if (type.IsSealed) { return "sealed"; }
            }

            // 
            return string.Empty;
        }
    }
}
