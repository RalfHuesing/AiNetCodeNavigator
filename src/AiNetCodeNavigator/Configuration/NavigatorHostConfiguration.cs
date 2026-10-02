using System.Text.Json;
using AiNetCodeNavigator.Logging;
using Serilog.Core;
using Serilog.Events;

namespace AiNetCodeNavigator.Configuration;

internal sealed record NavigatorHostConfigurationLoadResult(
    bool Succeeded,
    LogEventLevel MinimumLogLevel,
    string? ErrorCode = null,
    string? Message = null);

internal sealed class NavigatorHostConfiguration
{
    private readonly string path;
    private readonly bool isDefaultPath;
    private readonly LoggingLevelSwitch minimumLevelSwitch;

    internal NavigatorHostConfiguration(string path, bool isDefaultPath, LoggingLevelSwitch minimumLevelSwitch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(minimumLevelSwitch);
        this.path = System.IO.Path.GetFullPath(path);
        this.isDefaultPath = isDefaultPath;
        this.minimumLevelSwitch = minimumLevelSwitch;
    }

    internal async Task<NavigatorHostConfigurationLoadResult> LoadStartupAsync(CancellationToken cancellationToken)
    {
        if (isDefaultPath && !File.Exists(path))
        {
            LoggingSetup.SetMinimumLevel(minimumLevelSwitch, LogEventLevel.Information);
            return new NavigatorHostConfigurationLoadResult(true, LogEventLevel.Information);
        }

        if (!File.Exists(path))
        {
            return Failure("CONFIG_NOT_FOUND", "The configured host settings file does not exist.");
        }

        string? minimumLogLevel;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Failure("CONFIG_INVALID", "The host settings must be a JSON object with a minimumLogLevel string.");
            }

            var seenMinimumLogLevel = false;
            minimumLogLevel = null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!string.Equals(property.Name, "minimumLogLevel", StringComparison.Ordinal) || seenMinimumLogLevel)
                {
                    return Failure("CONFIG_INVALID", "The host settings contain an unsupported or duplicate field.");
                }

                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    return Failure("CONFIG_INVALID", "minimumLogLevel must be a string.");
                }

                seenMinimumLogLevel = true;
                minimumLogLevel = property.Value.GetString();
            }

            if (!seenMinimumLogLevel)
            {
                return Failure("CONFIG_INVALID", "The host settings must be a JSON object with a minimumLogLevel string.");
            }
        }
        catch (JsonException)
        {
            return Failure("CONFIG_INVALID", "The host settings file is malformed or contains unsupported fields.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure("CONFIG_UNAVAILABLE", "The host settings file could not be read.");
        }

        if (!TryParseLogLevel(minimumLogLevel, out var level))
        {
            return Failure("CONFIG_INVALID", "minimumLogLevel must be one of Verbose, Debug, Information, Warning, Error, or Fatal.");
        }

        LoggingSetup.SetMinimumLevel(minimumLevelSwitch, level);
        return new NavigatorHostConfigurationLoadResult(true, level);
    }

    private static NavigatorHostConfigurationLoadResult Failure(string code, string message) =>
        new(false, LogEventLevel.Information, code, message);

    private static bool TryParseLogLevel(string? value, out LogEventLevel level)
    {
        foreach (var candidate in new[] { "Verbose", "Debug", "Information", "Warning", "Error", "Fatal" })
        {
            if (string.Equals(value, candidate, StringComparison.Ordinal))
            {
                level = Enum.Parse<LogEventLevel>(candidate, ignoreCase: false);
                return true;
            }
        }

        level = default;
        return false;
    }
}
