using System;
using System.Collections.Generic;

namespace Documark
{
    public class ArgParseResult
    {
        private readonly Dictionary<string, List<string>> _options;
        private readonly string[] _args;

        public ArgParseResult(string[] args, IEnumerable<(string, string)> options)
        {
            if (options is null) { throw new ArgumentNullException(nameof(options)); }

            _args = args ?? throw new ArgumentNullException(nameof(args));

            _options = new Dictionary<string, List<string>>();
            foreach (var (name, value) in options)
            {
                if (!_options.TryGetValue(name, out var list))
                {
                    list = _options[name] = new List<string>();
                }

                list.Add(value);
            }
        }

        public IEnumerable<string> Options => _options.Keys;

        public IReadOnlyList<string> Arguments => _args;

        public IReadOnlyList<string> GetOption(string name)
        {
            if (_options.TryGetValue(name, out var val))
            {
                return val;
            }

            // No known option
            return null;
        }

        public bool HasOption(string v)
        {
            return GetOption(v) != null;
        }
    }
}
