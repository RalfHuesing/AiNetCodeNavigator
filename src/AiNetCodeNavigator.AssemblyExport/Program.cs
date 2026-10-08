namespace AiNetCodeNavigator.AssemblyExport;

internal static class Program
{
    internal static Task<int> Main(string[] args) => ExportCommandLine.InvokeAsync(args, RunAsync, Console.Out, Console.Error);

    private static async Task<int> RunAsync(ExportArguments parsed)
    {
        ExportPlan plan;
        try
        {
            plan = ExportPlanner.Create(parsed);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
                                         or BadImageFormatException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Export preflight failed: {exception.Message}");
            return 2;
        }

        if (parsed.DryRun)
        {
            Console.Out.WriteLine($"Dry run: {plan.Assemblies.Count} assemblies selected; output: {plan.OutputDirectory}");
            foreach (var item in plan.Assemblies)
            {
                Console.Out.WriteLine($"Selected ({(item.IsExplicit ? "source" : "dependency")}): {item.SourcePath}");
                foreach (var reference in item.FilteredReferences)
                    Console.Out.WriteLine($"Filtered reference: {reference.Reference.Name} ({reference.Rule})");
            }
            foreach (var exclusion in plan.Exclusions)
                Console.Out.WriteLine($"Excluded: {exclusion.SourcePath} ({exclusion.Rule})");
            foreach (var issue in plan.Issues)
                Console.Error.WriteLine($"Plan issue: {issue.Input}: {issue.Error}");
            if (plan.Assemblies.Count == 0)
                Console.Error.WriteLine("No managed assemblies remain selected.");
            return plan.Assemblies.Count == 0 || plan.Issues.Count > 0 ? 2 : 0;
        }

        if (plan.Assemblies.Count == 0)
        {
            Console.Error.WriteLine("No managed assemblies remain selected. The existing dump was not replaced.");
            foreach (var issue in plan.Issues) Console.Error.WriteLine($"Plan issue: {issue.Input}: {issue.Error}");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try { return await ExportRunner.RunAsync(plan, Console.Out, Console.Error, cancellation.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            var message = $"Export failed: {exception.Message}";
            Console.Error.WriteLine(message);
            return 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }
}
