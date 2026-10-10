using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Help;

namespace AiNetCodeNavigator.AssemblyExport;

internal enum ExportDependencyMode { All, None }

internal sealed record ExportArguments(string OutputDirectory, IReadOnlyList<string> Sources)
{
    internal IReadOnlyList<string> Includes { get; init; } = [];
    internal IReadOnlyList<string> Excludes { get; init; } = [];
    internal ExportDependencyMode Dependencies { get; init; } = ExportDependencyMode.All;
    internal bool DryRun { get; init; }
}

internal static class ExportCommandLine
{
    internal static async Task<int> InvokeAsync(string[] args, Func<ExportArguments, Task<int>> runAsync,
        TextWriter output, TextWriter errors)
    {
        var command = CreateCommand(out var readArguments);
        command.SetAction(parseResult => runAsync(readArguments(parseResult)));
        var result = command.Parse(args);
        var exitCode = await result.InvokeAsync(new InvocationConfiguration { Output = output, Error = errors }).ConfigureAwait(false);
        return result.Errors.Count > 0 && exitCode == 1 ? 2 : exitCode;
    }

    internal static bool TryParse(string[] args, out ExportArguments? parsed, out string? error)
    {
        var command = CreateCommand(out var readArguments);
        var result = command.Parse(args);
        error = result.Errors.Count > 0 ? string.Join(Environment.NewLine, result.Errors.Select(item => item.Message)) : null;
        if (error is null && result.Action is HelpAction or DocumentationAction)
            error = "Help and documentation do not describe an export invocation.";
        parsed = error is null ? readArguments(result) : null;
        return error is null;
    }

    private static RootCommand CreateCommand(out Func<ParseResult, ExportArguments> readArguments)
    {
        var output = Values("--output", "Marked output dump directory (required exactly once for export).", single: true);
        var source = Values("--source", "Source file or recursively searched directory (required for export); repeat for multiple sources.");
        var include = Values("--include", "Filename glob for source directories (* and ?); repeat to select more patterns.", glob: true);
        var exclude = Values("--exclude", "Filename glob excluded from all exports, including dependencies; repeat for more patterns.", glob: true);
        var dependencies = Values("--dependencies", "Additional referenced assemblies: all (default) or none.", single: true);
        dependencies.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string[]>()?.Any(value => value is not ("all" or "none")) == true)
                result.AddError("--dependencies must be 'all' or 'none'.");
        });
        var dryRun = new Option<bool>("--dry-run") { Description = "Show selection, exclusions and issues without writing or replacing the dump." };
        var documentation = new Option<string>("--doc")
        {
            Description = "Read embedded documentation: topics lists available topics; guide prints the guide.",
            Arity = ArgumentArity.ExactlyOne,
            HelpName = "topic",
        };
        documentation.Action = new DocumentationAction(documentation);
        var command = new RootCommand("Export readable source from selected managed assemblies without executing them. " +
            "Explore with --doc topics, then --doc guide. Documentation can be long; consider saving it to a file: " +
            "AiNetCodeNavigator.AssemblyExport.exe --doc guide > assembly-export.md")
        {
            output, source, include, exclude, dependencies, dryRun, documentation,
        };
        command.Validators.Add(result =>
        {
            if (result.GetResult(documentation) is not null)
            {
                if (new Option[] { output, source, include, exclude, dependencies, dryRun }.Any(option => result.GetResult(option) is not null))
                    result.AddError("--doc cannot be combined with export options. Run documentation and export separately.");
                return;
            }
            if (result.GetResult(output) is null) result.AddError("Option '--output' is required for export.");
            if (result.GetResult(source) is null) result.AddError("Option '--source' is required for export.");
        });
        readArguments = result => new ExportArguments(result.GetValue(output)![0], result.GetValue(source)!)
        {
            Includes = result.GetValue(include) ?? [],
            Excludes = result.GetValue(exclude) ?? [],
            Dependencies = result.GetValue(dependencies)?.FirstOrDefault() == "none" ? ExportDependencyMode.None : ExportDependencyMode.All,
            DryRun = result.GetValue(dryRun),
        };
        return command;
    }

    private sealed class DocumentationAction(Option<string> option) : SynchronousCommandLineAction
    {
        public override bool Terminating => true;
        public override bool ClearsParseErrors => false;

        public override int Invoke(ParseResult parseResult)
        {
            if (parseResult.Errors.Count > 0)
            {
                foreach (var error in parseResult.Errors)
                    parseResult.InvocationConfiguration.Error.WriteLine(error.Message);
                return 2;
            }
            var topic = parseResult.GetValue(option);
            var output = parseResult.InvocationConfiguration.Output;
            if (topic == "topics")
            {
                output.WriteLine("Embedded documentation topics:");
                output.WriteLine("  guide   Assembly selection, dependency policies, disposable output, agent navigation and exit codes.");
                output.WriteLine("Read with --doc guide. Documentation can be long; consider saving it to a file:");
                output.WriteLine("  AiNetCodeNavigator.AssemblyExport.exe --doc guide > assembly-export.md");
                return 0;
            }

            if (topic != "guide")
            {
                parseResult.InvocationConfiguration.Error.WriteLine($"Unknown documentation topic '{topic}'. Run --doc topics to list available topics.");
                return 2;
            }

            using var stream = typeof(ExportCommandLine).Assembly.GetManifestResourceStream("AiNetCodeNavigator.AssemblyExport.Documentation.export.md")
                ?? throw new InvalidOperationException("Embedded export documentation is missing.");
            using var reader = new StreamReader(stream);
            output.WriteLine("Documentation can be long; consider saving it to a file:");
            output.WriteLine("  AiNetCodeNavigator.AssemblyExport.exe --doc guide > assembly-export.md");
            output.WriteLine();
            output.Write(reader.ReadToEnd());
            return 0;
        }
    }

    private static Option<string[]> Values(string name, string description, bool single = false, bool glob = false)
    {
        var option = new Option<string[]>(name)
        {
            Description = description,
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false,
        };
        option.Validators.Add(result =>
        {
            var values = result.GetValueOrDefault<string[]>() ?? [];
            if (single && values.Length != 1) result.AddError($"{name} must be specified exactly once.");
            if (values.Any(string.IsNullOrWhiteSpace)) result.AddError($"{name} requires a non-empty value.");
            if (glob && values.Any(value => !ExportFilenamePattern.IsValid(value)))
                result.AddError($"{name} accepts filename globs with * and ? only, without paths, ** or invalid filename characters.");
        });
        return option;
    }
}
