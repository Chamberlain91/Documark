# Documark

> Documark, A .NET XML Documentation Convertor.

Documark is a command-line application to generate markdown documentation suitable for git repositories. 
It recursively scans the current or target directory for a pair of dotnet generated `.xml` with its associated `.dll`.

To generate the `.xml` documentation, add the following to your `*.csproj` file.
```xml
<GenerateDocumentationFile>true</GenerateDocumentationFile>
```

## Command Line

```
$> documark --help
Documark, A .NET XML Documentation Convertor.

        usage: documark [directory]

If the directory argument isn't specified, document will use the current directory.

--help [-h]
        Show this help

--output [-o]
        Set output directory name. [default: './Api']

--type [-t]
        Set output document type. [default: markdown]
```
