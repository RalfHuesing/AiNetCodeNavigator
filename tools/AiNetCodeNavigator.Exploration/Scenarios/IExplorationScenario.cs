namespace AiNetCodeNavigator.Exploration.Scenarios;

/// <summary>
/// Represents an executable exploration scenario for inspecting MCP tool interactions.
/// </summary>
internal interface IExplorationScenario
{
    /// <summary>
    /// Gets the unique name of the scenario used to invoke it via CLI or script.
    /// Defaults to the implementing class name.
    /// </summary>
    string Name => GetType().Name;

    /// <summary>
    /// Executes the scenario against the given exploration context.
    /// </summary>
    Task RunAsync(ExplorationContext context);
}
