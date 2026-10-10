namespace AiNetCodeNavigator.Cli;

internal static class EmbeddedDocumentation
{
    private static readonly (string Name, string Description)[] Topics =
    [
        ("overview", "Purpose, quick start, navigation examples, and limitations."),
        ("setup", "Installation, host settings, and MCP client configuration."),
        ("tools", "Navigation tool contracts, response recovery, and agent usage patterns."),
    ];

    internal static int Write(string topic, TextWriter output)
    {
        if (string.Equals(topic, "topics", StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine("Embedded documentation topics (no MCP server is started):");
            foreach (var entry in Topics)
                output.WriteLine($"  {entry.Name}: {entry.Description}");
            output.WriteLine("Read a topic with AiNetCodeNavigator.exe --doc <topic>.");
            output.WriteLine("Documents can be long. Consider saving stderr to a file before reading, for example:");
            output.WriteLine("  AiNetCodeNavigator.exe --doc tools 2> navigator-tools.md");
            return 0;
        }

        var matchedTopic = Topics.FirstOrDefault(entry => string.Equals(entry.Name, topic, StringComparison.OrdinalIgnoreCase));
        if (matchedTopic.Name is null)
        {
            output.WriteLine($"Unknown documentation topic '{topic}'. Use --doc topics to list available topics.");
            return 2;
        }

        output.WriteLine($"Embedded documentation: {matchedTopic.Name}");
        output.WriteLine("This document can be long. Consider saving stderr to a file before reading, for example:");
        output.WriteLine($"  AiNetCodeNavigator.exe --doc {matchedTopic.Name} 2> navigator-{matchedTopic.Name}.md");
        output.WriteLine("Repository-relative links refer to the source repository; use --doc topics for documents available offline.");
        output.WriteLine();
        using var stream = typeof(EmbeddedDocumentation).Assembly.GetManifestResourceStream($"AiNetCodeNavigator.Docs.{matchedTopic.Name}.md")
            ?? throw new InvalidOperationException($"Embedded documentation topic '{matchedTopic.Name}' is missing.");
        using var reader = new StreamReader(stream);
        output.Write(reader.ReadToEnd());
        return 0;
    }
}
