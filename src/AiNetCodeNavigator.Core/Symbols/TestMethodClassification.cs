#nullable enable

using System.Text.Json.Serialization;

namespace AiNetCodeNavigator.Core.Symbols;

public enum TestFrameworkKind
{
    Xunit,
    NUnit,
    MSTest
}

/// <summary>Static activity information; no value proves runtime execution.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TestActivityStatus>))]
public enum TestActivityStatus
{
    [JsonStringEnumMemberName("active")]
    Active,
    [JsonStringEnumMemberName("excluded")]
    Excluded,
    [JsonStringEnumMemberName("conditional")]
    Conditional
}

/// <summary>A recognized framework test method, independently of its project or file name.</summary>
public sealed record TestMethodClassification(TestFrameworkKind Framework, TestActivityStatus ActivityStatus);
