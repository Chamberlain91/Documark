using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Documark
{
    internal class ArgParse
    {
        private readonly Dictionary<string, string> _defaults;
        private readonly Dictionary<string, Option> _options;

        private Regex _arrayOption = new Regex(@"=\[(\w+(,\w+)*)\]$", RegexOptions.Compiled | RegexOptions.ECMAScript);

        public ArgParse()
        {
            _defaults = new Dictionary<string, string>();
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

            var values = Array.Empty<string>();
            var type = ArgumentType.Flag;

            var initial = default(string);

            // Is the option formated akin to "a=[b,c]"?
            var matchArray = _arrayOption.Match(name);
            if (matchArray.Success)
            {
                values = matchArray.Groups[1].Value.Split(',');
                initial = values[0]; // arrays always default to the first item

                // Extract name from formatted string
                var idx = name.IndexOf("=");
                name = name.Substring(0, idx);
                type = ArgumentType.Value;

                // Append default to description
                description += $" [default: '{initial}']";
            }
            else
            // Is the option formatted akin to "x="
            if (name.Contains("="))
            {
                var key = name;

                var idx = key.IndexOf("=");
                name = key.Substring(0, idx);
                type = ArgumentType.Value;

                // If text exists after the "=", extrct it as a default value.
                if (idx + 1 < key.Length)
                {
                    initial = key.Substring(idx + 1);
                }
            }

            // todo: validate name to be only letters

            // Store option
            _options[name] = new Option(name, alternatives, values, type, description);

            // Record alternatives
            foreach (var alt in alternatives)
            {
                _options[alt] = _options[name];
            }

            // 
            if (!string.IsNullOrWhiteSpace(initial))
            {
                _defaults[name] = initial;
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
                    _defaults.TryGetValue(opt.Name, out var def);
                    str += $"--{opt.Name} [{string.Join(", ", opt.Alternatives.Select(s => $"-{s}"))}]\n\t{opt.Description}{def}\n\n";
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
            var dict = new Dictionary<string, string>(_defaults);
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
                                var value = e.Current;

                                if (option.Values.Count > 0)
                                {
                                    // We expect a value from a fixed set
                                    if (!option.Values.Contains(value))
                                    {
                                        throw new ArgParseException($"Option '{option.Name}' must be one of [{string.Join(", ", option.Values)}]");
                                    }
                                }

                                // Append parsed option
                                dict[option.Name] = value;
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
                            dict[option.Name] = null;
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

            // 
            var output = new List<(string, string)>();
            foreach (var (key, val) in dict)
            {
                output.Add((key, val));
            }

            return new ArgParseResult(args.ToArray(), output);
        }

        private class Option
        {
            public Option(string name, string[] alternatives, string[] values, ArgumentType type, string description)
            {
                Alternatives = alternatives;
                Values = values;
                Type = type;
                Name = name;
                Description = description;
            }

            public IReadOnlyList<string> Values { get; }

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
