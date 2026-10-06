namespace AiNetCodeNavigator.AssemblyExport;

internal sealed record ExportArguments(string OutputDirectory, IReadOnlyList<string> Sources);

internal static class ExportCommandLine
{
    internal const string Usage = "Usage: AiNetCodeNavigator.AssemblyExport.exe <output-directory> <source-dll-or-pattern> [<source-dll-or-pattern> ...]";

    internal static bool TryParse(string[] args, out ExportArguments? parsed, out string? error)
    {
        parsed = null;
        error = null;
        if (args.Length < 2 || args.Any(string.IsNullOrWhiteSpace))
        {
            error = "An output directory and at least one DLL path or pattern are required.";
            return false;
        }

        if (args[0] is "--help" or "-h" || args.Skip(1).Any(arg => arg is "--help" or "-h"))
        {
            error = "Help must be requested without other arguments.";
            return false;
        }

        parsed = new ExportArguments(args[0], args.Skip(1).ToArray());
        return true;
    }
}
