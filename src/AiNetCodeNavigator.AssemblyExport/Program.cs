namespace AiNetCodeNavigator.AssemblyExport;

internal static class Program
{
    internal static async Task<int> Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            Console.Out.WriteLine(ExportCommandLine.Usage);
            return 0;
        }

        if (!ExportCommandLine.TryParse(args, out var parsed, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(ExportCommandLine.Usage);
            return 2;
        }

        ExportPlan plan;
        try
        {
            plan = ExportPlanner.Create(parsed!);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException
                                         or BadImageFormatException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Export preflight failed: {exception.Message}");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try { return await ExportRunner.RunAsync(plan, Console.Out, Console.Error, cancellation.Token).ConfigureAwait(false); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Console.Error.WriteLine($"Export failed: {exception.Message}");
            return 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }
}
