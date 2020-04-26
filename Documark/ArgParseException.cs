using System;

namespace Documark
{
    /// <summary>
    /// Exception thrown when <see cref="ArgParse"/> fails to parse arguments.
    /// </summary>
    public class ArgParseException : Exception
    {
        /// <summary>
        /// Construct a new <see cref="ArgParseException"/>.
        /// </summary>
        public ArgParseException()
        { }

        /// <summary>
        /// Construct a new <see cref="ArgParseException"/>.
        /// </summary>
        public ArgParseException(string message)
            : base(message)
        { }
    }
}
