using System;

namespace Documark
{
    public static class Log
    {
        public static void Error(string message)
        {
            var f = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ForegroundColor = f;
        }

        public static void Warning(string message)
        {
            var f = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ForegroundColor = f;
        }
    }
}
