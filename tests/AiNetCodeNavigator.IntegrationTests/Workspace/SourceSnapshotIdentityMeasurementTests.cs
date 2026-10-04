#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using AiNetCodeNavigator.TestKit;
using Xunit;

namespace AiNetCodeNavigator.IntegrationTests.Workspace;

[Trait("Category", "Integration")]
[Trait("Category", "Measurement")]
public sealed class SourceSnapshotIdentityMeasurementTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ReportsIdentityHashSeparatelyFromResidentDiskRefresh()
    {
        using var fixture = TestTempDirectory.Create("ainet-source-identity-measurement-");
        fixture.CreateFile("src/App/App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework>"
            + "</PropertyGroup></Project>");
        fixture.CreateFile("src/App/App.cs", "namespace IdentityMeasurement; public sealed class Consumer { public int Read() => 1; }");
        var solutionPath = fixture.GetPath("IdentityMeasurement.slnx");
        await File.WriteAllTextAsync(solutionPath, "<Solution><Project Path=\"src/App/App.csproj\" /></Solution>");

        await using var resident = MSBuildSolutionLoader.CreateResidentSolution(solutionPath);
        var setup = await resident.GetCurrentSnapshotAsync();
        Assert.True(setup.Succeeded, setup.Error?.Message);
        var solution = Assert.IsType<Microsoft.CodeAnalysis.Solution>(setup.Solution);
        var inputs = Assert.IsType<SourceIdentityValidatedInputs>(setup.IdentityInputs);
        var validated = new SourceIdentityValidatedSnapshot(solution, inputs);

        var warmHash = await SourceAnalysisIdentityEncoder.ComputeAsync(validated, CancellationToken.None);
        Assert.True(warmHash.IsSuccess, warmHash.Error?.Message);
        var warmRefresh = await resident.GetCurrentSnapshotAsync();
        Assert.True(warmRefresh.Succeeded, warmRefresh.Error?.Message);
        Assert.Same(solution, warmRefresh.Solution);

        var hashMilliseconds = new List<double>(5);
        var refreshMilliseconds = new List<double>(5);
        for (var index = 0; index < 5; index++)
        {
            var hashTimer = Stopwatch.StartNew();
            var hash = await SourceAnalysisIdentityEncoder.ComputeAsync(validated, CancellationToken.None);
            hashTimer.Stop();
            Assert.True(hash.IsSuccess, hash.Error?.Message);
            Assert.Equal(warmHash.Value!.ContentHash, hash.Value!.ContentHash);
            hashMilliseconds.Add(hashTimer.Elapsed.TotalMilliseconds);

            var refreshTimer = Stopwatch.StartNew();
            var refreshed = await resident.GetCurrentSnapshotAsync();
            refreshTimer.Stop();
            Assert.True(refreshed.Succeeded, refreshed.Error?.Message);
            Assert.Same(solution, refreshed.Solution);
            refreshMilliseconds.Add(refreshTimer.Elapsed.TotalMilliseconds);
        }

        output.WriteLine("Source identity measurement (untimed MSBuild load and warmup; five sequential runs; no pass/fail thresholds):");
        output.WriteLine($"identity hash only, five uncached computations from one validated snapshot: {Format(hashMilliseconds)} ms");
        output.WriteLine($"resident freshness refresh only, same snapshot: {Format(refreshMilliseconds)} ms");
    }

    private static string Format(IReadOnlyList<double> values) => string.Join(", ", values.Select(value => value.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)));
}
