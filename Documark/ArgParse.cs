using System;
using System.Collections.Generic;
using System.Linq;

namespace Documark
{
    internal class ArgParse
    {
        private readonly Dictionary<string, Option> _options;

        public ArgParse()
        {
            _options = new Dictionary<string, Option>();
            AddOption("help", "h", "Show this help");
        }

        public void AddOption(string name, string alternative, string description)
        {
            var alternatives = alternative == null ? null : new[] { alternative };
            AddOption(name, alternatives, description);
        }

        public void AddOption(string name, string[] alternatives, string description)
        {
            if (alternatives == null) { alternatives = Array.Empty<string>(); }

            var type = ArgumentType.Flag;
            if (name.EndsWith("="))
            {
                type = ArgumentType.Value;
                name = name.Substring(0, name.Length - 1);
            }

            // Store option
            _options[name] = new Option(name, alternatives, type, description);

            // Record alternatives
            foreach (var alt in alternatives)
            {
                _options[alt] = _options[name];
            }
        }

        internal string GetHelp()
        {
            var str = "";

            foreach (var opt in _options.Values.Distinct())
            {
                if (opt.Alternatives.Count == 0)
                {
                    str += $"--{opt.Name}";
                }
                else
                {
                    str += $"--{opt.Name} [{string.Join(", ", opt.Alternatives.Select(s => $"-{s}"))}]\n\t{opt.Description}\n\n";
                }
            }

            return str;
        }

        private Option GetOption(string name)
        {
            if (_options.TryGetValue(name, out var opt))
            {
                return opt;
            }

            return null;
        }

        /**
         * NOTE TO SELF: Markdown Nested by Hierachy. ie, DistortShader extends Shader... so DistortShader is somehow nested into Shader.
         */

        public ArgParseResult Parse(string[] arguments)
        {
            var dict = new List<(string, string)>();
            var args = new List<string>();

            var e = ((IEnumerable<string>) arguments).GetEnumerator();
            while (e.MoveNext())
            {
                var arg = e.Current;
                if (arg.StartsWith('-')) // ie, -h
                {
                    var name = arg.Substring(1);
                    var expectFullName = false;

                    // ie, --help
                    if (name.StartsWith('-'))
                    {
                        name = name.Substring(1);
                        expectFullName = true;
                    }

                    // Get option
                    var option = GetOption(name);

                    if (option != null)
                    {
                        // If name/switch syntax is misaligned
                        if (!expectFullName && option.Name == name || expectFullName && option.Name != name)
                        {
                            string guide;
                            if (expectFullName) { guide = $"--{option.Name}"; }
                            else { guide = string.Join(", ", option.Alternatives.Select(s => $"-{s}")); }
                            throw new ArgParseException($"Invalid option syntax '{name}'. Did you mean '{guide}'?");
                        }

                        if (option.Type == ArgumentType.Value)
                        {
                            if (e.MoveNext())
                            {
                                // Append parsed option
                                dict.Add((option.Name, e.Current));
                            }
                            else
                            {
                                // Needed a value and didn't have any, act like the switch didn't exist?
                                throw new ArgParseException($"Option '{option.Name}' required value");
                            }
                        }
                        else
                        {
                            // Append parsed switch
                            dict.Add((option.Name, null));
                        }
                    }
                    else
                    {
                        throw new ArgParseException($"Unknown option '{name}'");
                    }
                }
                else
                {
                    args.Add(arg);
                }
            }

            return new ArgParseResult(args.ToArray(), dict);
        }

        private class Option
        {
            public Option(string name, string[] alternatives, ArgumentType type, string description)
            {
                Alternatives = alternatives;
                Type = type;
                Name = name;
                Description = description;
            }

            public IReadOnlyList<string> Alternatives { get; }

            public ArgumentType Type { get; }

            public string Name { get; }

            public string Description { get; }
        }

        private enum ArgumentType
        {
            Flag,
            Value
        }
    }
}
