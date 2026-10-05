namespace AiNetCodeNavigator.Exploration.Scenarios;

/// <summary>
/// Automatically discovers and exposes all exploration scenarios implemented across the assembly.
/// </summary>
internal static class ScenarioRegistry
{
    private static readonly Lazy<IReadOnlyDictionary<string, Func<ExplorationContext, Task>>> ScenariosLazy =
        new(DiscoverScenarios);

    /// <summary>
    /// Gets all registered scenarios keyed by name (case-insensitive).
    /// </summary>
    internal static IReadOnlyDictionary<string, Func<ExplorationContext, Task>> All => ScenariosLazy.Value;

    private static IReadOnlyDictionary<string, Func<ExplorationContext, Task>> DiscoverScenarios()
    {
        var scenarios = new Dictionary<string, Func<ExplorationContext, Task>>(StringComparer.OrdinalIgnoreCase);

        var scenarioTypes = typeof(ScenarioRegistry).Assembly.GetTypes()
            .Where(type => !type.IsAbstract && typeof(IExplorationScenario).IsAssignableFrom(type));

        foreach (var type in scenarioTypes)
        {
            if (type.GetConstructor(Type.EmptyTypes) is not null &&
                Activator.CreateInstance(type) is IExplorationScenario scenario)
            {
                scenarios[scenario.Name] = scenario.RunAsync;
            }
        }

        return scenarios;
    }
}
