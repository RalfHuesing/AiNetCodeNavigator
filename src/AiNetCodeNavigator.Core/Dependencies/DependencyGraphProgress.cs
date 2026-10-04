using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Dependencies;

internal abstract record DependencyGraphProgress;
internal sealed record DependencyCollectionStarted : DependencyGraphProgress;
internal sealed record DependencyRequiredDocumentsKnown(int TotalDocuments) : DependencyGraphProgress;
internal sealed record DependencyDocumentSatisfied(long SnapshotTicket, ProjectId ProjectId, DocumentId DocumentId) : DependencyGraphProgress;
