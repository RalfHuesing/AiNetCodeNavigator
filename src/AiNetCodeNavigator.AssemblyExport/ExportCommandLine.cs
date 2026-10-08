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
        if (error is null && result.Action is HelpAction)
            error = "Help does not describe an export invocation.";
        parsed = error is null ? readArguments(result) : null;
        return error is null;
    }

    private static RootCommand CreateCommand(out Func<ParseResult, ExportArguments> readArguments)
    {
        var output = Values("--output", "Marked output dump directory (required exactly once).", required: true, single: true);
        var source = Values("--source", "Source file or recursively searched directory; repeat for multiple sources.", required: true);
        var include = Values("--include", "Filename glob for source directories (* and ?); repeat to select more patterns.", glob: true);
        var exclude = Values("--exclude", "Filename glob excluded from all exports, including dependencies; repeat for more patterns.", glob: true);
        var dependencies = Values("--dependencies", "Additional referenced assemblies: all (default) or none.", single: true);
        dependencies.Validators.Add(result =>
        {
            if (result.GetValueOrDefault<string[]>()?.Any(value => value is not ("all" or "none")) == true)
                result.AddError("--dependencies must be 'all' or 'none'.");
        });
        var dryRun = new Option<bool>("--dry-run") { Description = "Show selection, exclusions and issues without writing or replacing the dump." };
        var command = new RootCommand("Export readable source from selected managed assemblies.")
        {
            output, source, include, exclude, dependencies, dryRun,
        };
        readArguments = result => new ExportArguments(result.GetValue(output)![0], result.GetValue(source)!)
        {
            Includes = result.GetValue(include) ?? [],
            Excludes = result.GetValue(exclude) ?? [],
            Dependencies = result.GetValue(dependencies)?.FirstOrDefault() == "none" ? ExportDependencyMode.None : ExportDependencyMode.All,
            DryRun = result.GetValue(dryRun),
        };
        return command;
    }

    private static Option<string[]> Values(string name, string description, bool required = false, bool single = false, bool glob = false)
    {
        var option = new Option<string[]>(name)
        {
            Description = description,
            Required = required,
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
