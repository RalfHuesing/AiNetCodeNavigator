#nullable enable

namespace AiNetCodeNavigator.Core.Workspace;

public sealed record ProjectLeaseResult
{
    public bool Succeeded => Lease is not null;

    public ProjectLease? Lease { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public static ProjectLeaseResult Success(ProjectLease lease) => new() { Lease = lease };

    public static ProjectLeaseResult Failure(string errorCode, string errorMessage) =>
        new() { ErrorCode = errorCode, ErrorMessage = errorMessage };
}
