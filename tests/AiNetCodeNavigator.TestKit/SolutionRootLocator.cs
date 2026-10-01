#nullable enable

using System;
using System.IO;

namespace AiNetCodeNavigator.TestKit;

/// <summary>
/// Finds the solution root directory starting from the current application directory.
/// </summary>
public static class SolutionRootLocator
{
    /// <summary>
    /// Locates the directory containing <c>AiNetCodeNavigator.slnx</c>.
    /// </summary>
    public static string Find()
    {
        var currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "AiNetCodeNavigator.slnx")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        throw new DirectoryNotFoundException("The root directory containing the solution 'AiNetCodeNavigator.slnx' was not found.");
    }
}
