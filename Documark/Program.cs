using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Documark
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            var parser = new ArgParse(); // todo: some sort of arguments syntax "[directory]"
            parser.AddOption("output=./Api", "o", "Set output directory name.");
            parser.AddOption("type=[markdown,html]", "t", "Set output document type.");
            parser.AddOption("verbose", "v", "Emit verbose messages.");
            // todo: type=[markdown, json] and automatically generate [default: markdown]
            //       then the parser can validate options from the list.

            try
            {
                // Try to parse arguments
                var command = parser.Parse(args);
                if (command != null)
                {
                    // Requesting the help screen
                    if (command.HasOption("help"))
                    {
                        Console.WriteLine("Documark, A .NET XML Documentation Convertor.\n");
                        Console.WriteLine("\tusage: documark [directory]\n");
                        Console.WriteLine("If the directory argument isn't specified, document will use the current directory.\n");
                        Console.WriteLine(parser.GetHelp());
                    }
                    else
                    {
                        // Set verbosity
                        Log.IsVerbose = command.HasOption("verbose");

                        // Assume the current directory by default
                        var dir = Directory.GetCurrentDirectory();
                        if (command.Arguments.Count == 1)
                        {
                            // A directory argument was given
                            dir = command.Arguments[0];
                        }
                        else if (command.Arguments.Count > 1)
                        {
                            throw new ArgParseException("Too many arguments!");
                        }

                        // Get the full path to the directory
                        dir = Path.GetFullPath(dir);

                        // Does this directory exist?
                        if (Directory.Exists(dir))
                        {
                            // Scan directory for .xml/.dll pairs
                            var assemblies = FindAndLoadDocumentedAssemblies(dir);
                            if (assemblies.Count > 0)
                            {
                                var generatorType = command.GetOption("type")[0];
                                var outputDirectory = command.GetOption("output")[0];

                                // Collect documentation
                                foreach (var assembly in assemblies)
                                {
                                    // Load XML Documentation
                                    Documentation.LoadDocumentation(assembly);
                                }

                                Log.Info($"----");

                                // Construct generator
                                var generator = CreateGenerator(outputDirectory, generatorType);

                                // Emit documentation
                                foreach (var assembly in assemblies)
                                {
                                    Console.WriteLine($"Generating: {assembly.GetName().Name}");
                                    generator.Generate(assembly);
                                }
                            }
                            else
                            {
                                Console.WriteLine("No XML documentation found.");
                            }
                        }
                        else
                        {
                            throw new ArgParseException("Unknown directory.");
                        }
                    }
                }
            }
            catch (ArgParseException e)
            {
                Log.Error(e.Message);
                Console.WriteLine("Try 'documark --help' for more information.");
            }
        }

        private static Generator CreateGenerator(string outputDirectory, string generatorType)
        {
            return generatorType switch
            {
                "html" => new HtmlGenerator(outputDirectory),
                _ => new MarkdownGenerator(outputDirectory),
            };
        }

        private static HashSet<Assembly> FindAndLoadDocumentedAssemblies(string directory)
        {
            var assemblies = new HashSet<Assembly>();
            var names = new HashSet<string>();
            foreach (var path in Directory.EnumerateFiles(directory, "*.xml", SearchOption.AllDirectories)
                                          .OrderBy(s => s.Contains("/bin") ? 1 : -1))
            {
                // Gets the similarly named .dll from the .xml file path 
                var assemblyPath = Path.ChangeExtension(path, "dll");

                // Does this .dll actually exist?
                if (File.Exists(assemblyPath))
                {
                    var name = AssemblyName.GetAssemblyName(assemblyPath);
                    if (names.Add(name.Name))
                    {
                        Log.Info($"Loading: {name.Name}");

                        try
                        {
                            // Attempt loading the assembly
                            var assembly = Assembly.LoadFrom(assemblyPath);
                            assemblies.Add(assembly); // success!
                        }
                        catch (Exception e) when (e is BadImageFormatException ||
                                                  e is FileLoadException)
                        {
                            // Eh, this is ok...
                            Log.Error($"Unable to load '{assemblyPath}' ({e.Message}).");
                        }
                    }
                }
            }

            return assemblies;
        }
    }
}
