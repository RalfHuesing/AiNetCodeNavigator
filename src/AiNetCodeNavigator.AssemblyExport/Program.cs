namespace AiNetCodeNavigator.AssemblyExport;

internal static class Program
{
    internal static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] is "--help" or "-h")
        {
            Console.Out.WriteLine(ExportCommandLine.Usage);
            return 0;
        }

        if (!ExportCommandLine.TryParse(args, out _, out var error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(ExportCommandLine.Usage);
            return 2;
        }

        Console.Error.WriteLine("Assembly export is not available in this build.");
        return 1;
    }
}
