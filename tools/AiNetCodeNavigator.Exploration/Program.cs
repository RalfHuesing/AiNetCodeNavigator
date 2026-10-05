using System.Globalization;
using AiNetCodeNavigator.Exploration;
using AiNetCodeNavigator.Exploration.Scenarios;
using AiNetCodeNavigator.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (args.Length == 1 && args[0] == "--list")
{
    foreach (var name in ScenarioRegistry.All.Keys) Console.WriteLine(name);
    return 0;
}

if (args.Length != 3 || !ScenarioRegistry.All.TryGetValue(args[1], out var scenario))
{
    Console.Error.WriteLine("Usage: exploration <repository-root> <scenario> <timeout-seconds>, or --list.");
    return 2;
}

var repositoryRoot = Path.GetFullPath(args[0]);
var explorationRoot = Path.Combine(repositoryRoot, "temp", "exploration");
Directory.CreateDirectory(explorationRoot);
var outputDirectory = Path.Combine(explorationRoot, args[1],
    DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture));
Directory.CreateDirectory(outputDirectory);
Console.WriteLine($"Artifacts: {outputDirectory}");
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(int.Parse(args[2], CultureInfo.InvariantCulture)));
var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
builder.Logging.ClearProviders();
using var host = builder.Build();
await using var runtime = new NavigatorHostRuntime(host.Services.GetRequiredService<IHostApplicationLifetime>());
try
{
    var context = new ExplorationContext(runtime, repositoryRoot, outputDirectory, timeout.Token);
    await scenario(context).ConfigureAwait(false);
    Console.WriteLine("Technical execution succeeded. Inspect the artifacts for content quality.");
    return 0;
}
catch (Exception exception)
{
    await File.WriteAllTextAsync(Path.Combine(outputDirectory, "error.txt"), exception.ToString(), CancellationToken.None).ConfigureAwait(false);
    Console.Error.WriteLine($"Technical execution failed: {exception.Message}");
    return 1;
}
