using System.Text.Json;
using AiNetCodeNavigator.Logging;
using Serilog.Core;
using Serilog.Events;

namespace AiNetCodeNavigator.Configuration;

internal sealed record NavigatorHostSettingsSnapshot(long Version, LogEventLevel MinimumLogLevel);

internal sealed record ConfigurationReloadResult(bool Succeeded, NavigatorHostSettingsSnapshot Settings, string? ErrorCode = null, string? Message = null);

internal sealed class NavigatorHostConfiguration : IDisposable
{
    private readonly string path;
    private readonly bool isDefaultPath;
    private readonly LoggingLevelSwitch minimumLevelSwitch;
    private readonly SemaphoreSlim reloadGate = new(1, 1);
    private readonly object stateGate = new();
    private NavigatorHostSettingsSnapshot current = new(0, LogEventLevel.Information);

    internal NavigatorHostConfiguration(string path, bool isDefaultPath, LoggingLevelSwitch minimumLevelSwitch)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(minimumLevelSwitch);
        this.path = System.IO.Path.GetFullPath(path);
        this.isDefaultPath = isDefaultPath;
        this.minimumLevelSwitch = minimumLevelSwitch;
    }

    internal string Path => path;
    internal NavigatorHostSettingsSnapshot Current
    {
        get
        {
            lock (stateGate)
            {
                return current;
            }
        }
    }

    internal async Task<ConfigurationReloadResult> LoadStartupAsync(CancellationToken cancellationToken)
    {
        if (isDefaultPath && !File.Exists(path))
        {
            var defaults = new NavigatorHostSettingsSnapshot(1, LogEventLevel.Information);
            lock (stateGate)
            {
                LoggingSetup.SetMinimumLevel(minimumLevelSwitch, defaults.MinimumLogLevel);
                Volatile.Write(ref current, defaults);
            }
            return new ConfigurationReloadResult(true, defaults);
        }

        return await ReloadAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<ConfigurationReloadResult> ReloadAsync(CancellationToken cancellationToken)
    {
        await reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
            {
                return Failure("CONFIG_NOT_FOUND", "The configured host settings file does not exist.");
            }

            NavigatorHostConfigurationFile? candidate;
            try
            {
                await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return Failure("CONFIG_INVALID", "The host settings must be a JSON object with a minimumLogLevel string.");
                }

                var seenMinimumLogLevel = false;
                string? minimumLogLevel = null;
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

                candidate = seenMinimumLogLevel ? new NavigatorHostConfigurationFile(minimumLogLevel) : null;
            }
            catch (JsonException)
            {
                return Failure("CONFIG_INVALID", "The host settings file is malformed or contains unsupported fields.");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return Failure("CONFIG_UNAVAILABLE", "The host settings file could not be read.");
            }

            if (candidate is null || !TryParseLogLevel(candidate.MinimumLogLevel, out var level))
            {
                return Failure("CONFIG_INVALID", "minimumLogLevel must be one of Verbose, Debug, Information, Warning, Error, or Fatal.");
            }

            NavigatorHostSettingsSnapshot next;
            lock (stateGate)
            {
                next = new NavigatorHostSettingsSnapshot(current.Version + 1, level);
                LoggingSetup.SetMinimumLevel(minimumLevelSwitch, level);
                Volatile.Write(ref current, next);
            }
            return new ConfigurationReloadResult(true, next);
        }
        finally
        {
            reloadGate.Release();
        }
    }

    private ConfigurationReloadResult Failure(string code, string message) => new(false, Current, code, message);

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

    public void Dispose()
    {
        reloadGate.Dispose();
    }

    private sealed record NavigatorHostConfigurationFile(string? MinimumLogLevel);
}
