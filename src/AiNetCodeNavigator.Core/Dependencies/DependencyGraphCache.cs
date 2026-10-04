#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AiNetCodeNavigator.Core.Symbols;
using AiNetCodeNavigator.Core.Workspace;
using Microsoft.CodeAnalysis;

namespace AiNetCodeNavigator.Core.Dependencies;

/// <summary>Runtime-owned retention and single-flight for immutable source dependency facts.</summary>
internal sealed class DependencyGraphCache : IAsyncDisposable
{
    internal const int DefaultMaximumBuckets = 4;
    internal const long DefaultMaximumRetainedBytes = 32L * 1024 * 1024;
    internal static readonly TimeSpan DefaultIdleTtl = TimeSpan.FromMinutes(10);

    private readonly record struct BucketKey(string TargetPath, long SnapshotTicket, SymbolScopeType ScopeType, bool IncludeGenerated);
    private readonly record struct NeedKey(BucketKey Bucket, DependencyDocumentIdentity Document);

    private sealed class Bucket(BucketKey key, long lastAccess)
    {
        internal BucketKey Key { get; } = key;
        internal long LastAccess { get; set; } = lastAccess;
        internal Dictionary<DependencyDocumentIdentity, DependencyDocumentFact> Facts { get; } =
            new(DependencyDocumentIdentityComparer.Instance);
        internal Dictionary<string, string> Strings { get; } = new(StringComparer.Ordinal);
        internal long PayloadBytes { get; set; }
    }

    private sealed class NeedSlot(NeedKey key, DependencyDocumentWorkItem item)
    {
        internal NeedKey Key { get; } = key;
        internal DependencyDocumentWorkItem Item { get; } = item;
        internal TaskCompletionSource<DependencyDocumentScanOutcome> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CollectionBatch? Batch { get; set; }
        internal int SubscriberCount { get; set; }
        internal bool Quiescing { get; set; }
        internal Task<DependencyDocumentScanOutcome> Task => Completion.Task;
    }

    private sealed class CollectionBatch(BucketKey key, DependencyGraphCollectionPlan plan, NeedSlot[] slots, CancellationToken shutdown)
    {
        internal BucketKey Key { get; } = key;
        internal DependencyGraphCollectionPlan Plan { get; } = plan;
        internal NeedSlot[] Slots { get; } = slots;
        internal CancellationTokenSource Cancellation { get; } = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        internal Task? Task { get; set; }
        internal bool Closing { get; set; }
        internal bool Retired { get; set; }
        internal int SemanticScanCount;
    }

    private sealed record TargetRetirement(string TargetPath, long MaximumTicket)
    {
        internal TaskCompletionSource Drained { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class DependencyDocumentIdentityComparer : IEqualityComparer<DependencyDocumentIdentity>
    {
        internal static DependencyDocumentIdentityComparer Instance { get; } = new();

        public bool Equals(DependencyDocumentIdentity? left, DependencyDocumentIdentity? right) =>
            ReferenceEquals(left, right) || left is not null && right is not null
            && string.Equals(left.OwnerProjectPath, right.OwnerProjectPath, StringComparison.Ordinal)
            && string.Equals(left.OwnerContextFingerprint, right.OwnerContextFingerprint, StringComparison.Ordinal)
            && string.Equals(left.DocumentPath, right.DocumentPath, StringComparison.Ordinal)
            && string.Equals(left.Name, right.Name, StringComparison.Ordinal)
            && left.Folders.SequenceEqual(right.Folders, StringComparer.Ordinal)
            && string.Equals(left.SourceCodeKind, right.SourceCodeKind, StringComparison.Ordinal)
            && string.Equals(left.TextHash, right.TextHash, StringComparison.Ordinal)
            && left.DuplicateOrdinal == right.DuplicateOrdinal;

        public int GetHashCode(DependencyDocumentIdentity identity)
        {
            var hash = new HashCode();
            hash.Add(identity.OwnerProjectPath, StringComparer.Ordinal);
            hash.Add(identity.OwnerContextFingerprint, StringComparer.Ordinal);
            hash.Add(identity.DocumentPath, StringComparer.Ordinal);
            hash.Add(identity.Name, StringComparer.Ordinal);
            foreach (var folder in identity.Folders) hash.Add(folder, StringComparer.Ordinal);
            hash.Add(identity.SourceCodeKind, StringComparer.Ordinal);
            hash.Add(identity.TextHash, StringComparer.Ordinal);
            hash.Add(identity.DuplicateOrdinal);
            return hash.ToHashCode();
        }
    }

    private sealed class NeedKeyComparer : IEqualityComparer<NeedKey>
    {
        internal static NeedKeyComparer Instance { get; } = new();

        public bool Equals(NeedKey left, NeedKey right) =>
            left.Bucket == right.Bucket && DependencyDocumentIdentityComparer.Instance.Equals(left.Document, right.Document);

        public int GetHashCode(NeedKey key) => HashCode.Combine(key.Bucket, DependencyDocumentIdentityComparer.Instance.GetHashCode(key.Document));
    }

    private readonly object gate = new();
    private readonly Dictionary<BucketKey, Bucket> retainedBuckets = [];
    private readonly Dictionary<NeedKey, NeedSlot> inFlight = new(NeedKeyComparer.Instance);
    private readonly Dictionary<long, CollectionBatch> activeBatches = [];
    private readonly List<TargetRetirement> targetRetirements = [];
    private readonly TimeProvider timeProvider;
    private readonly int maximumBuckets;
    private readonly long maximumRetainedBytes;
    private readonly TimeSpan idleTtl;
    private readonly DependencyGraphCollectionObserver? observer;
    private readonly CancellationTokenSource shutdown;
    private readonly TaskCompletionSource disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long nextBatchId;
    private bool disposeStarted;

    internal DependencyGraphCache(
        CancellationToken runtimeLifetime = default,
        TimeProvider? timeProvider = null,
        int maxBuckets = DefaultMaximumBuckets,
        long maxRetainedBytes = DefaultMaximumRetainedBytes,
        TimeSpan? idleTtl = null,
        DependencyGraphCollectionObserver? observer = null)
    {
        if (maxBuckets < 0) throw new ArgumentOutOfRangeException(nameof(maxBuckets));
        if (maxRetainedBytes < 0) throw new ArgumentOutOfRangeException(nameof(maxRetainedBytes));
        if (idleTtl is { } ttl && ttl <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(idleTtl));
        this.timeProvider = timeProvider ?? TimeProvider.System;
        maximumBuckets = maxBuckets;
        maximumRetainedBytes = maxRetainedBytes;
        this.idleTtl = idleTtl ?? DefaultIdleTtl;
        this.observer = observer;
        shutdown = CancellationTokenSource.CreateLinkedTokenSource(runtimeLifetime);
    }

    internal async Task<DependencyGraphCollection> CollectAsync(
        Solution solution,
        DependencyGraphCollectionOptions options,
        string canonicalTargetPath,
        long snapshotTicket,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<ProjectId, string>? ownerContextFingerprints = null)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(canonicalTargetPath)) throw new ArgumentException("A canonical target path is required.", nameof(canonicalTargetPath));
        if (snapshotTicket <= 0) throw new ArgumentOutOfRangeException(nameof(snapshotTicket));
        if (!Enum.IsDefined(options.ScopeType)) throw new ArgumentOutOfRangeException(nameof(options));

        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
        var requestToken = requestCancellation.Token;
        var targetPath = CanonicalizeTarget(canonicalTargetPath);
        var plan = await DependencyGraphScanner.PrepareCollectionPlanAsync(solution, options, ownerContextFingerprints, requestToken)
            .ConfigureAwait(false);
        return await CollectPlanAsync(plan, targetPath, snapshotTicket, requestToken).ConfigureAwait(false);
    }

    internal DependencyGraphCollection? GetFullRetainedCollection(
        DependencyGraphCollectionPlan plan, string targetPath, long snapshotTicket)
    {
        var key = new BucketKey(CanonicalizeTarget(targetPath), snapshotTicket, plan.ScopeType, plan.IncludeGenerated);
        var facts = new List<DependencyDocumentFact>();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposeStarted, this);
            shutdown.Token.ThrowIfCancellationRequested();
            ExpireBucketsLocked(timeProvider.GetTimestamp());
            if (targetRetirements.Any(retirement => retirement.TargetPath == key.TargetPath && snapshotTicket <= retirement.MaximumTicket)
                || !retainedBuckets.TryGetValue(key, out var bucket)) return null;
            bucket.LastAccess = timeProvider.GetTimestamp();
            foreach (var item in plan.RequiredDocuments)
            {
                if (!bucket.Facts.TryGetValue(item.Identity, out var fact)) return null;
                facts.Add(fact);
            }
        }
        return DependencyGraphScanner.CreateCollectionFromFacts(plan, facts, [], 0);
    }

    internal async Task<DependencyGraphCollection> CollectPlanAsync(
        DependencyGraphCollectionPlan plan, string targetPath, long snapshotTicket,
        CancellationToken cancellationToken = default)
    {
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, shutdown.Token);
        var requestToken = requestCancellation.Token;
        targetPath = CanonicalizeTarget(targetPath);
        requestToken.ThrowIfCancellationRequested();
        var bucketKey = new BucketKey(targetPath, snapshotTicket, plan.ScopeType, plan.IncludeGenerated);
        var facts = new Dictionary<DependencyDocumentIdentity, DependencyDocumentFact>(DependencyDocumentIdentityComparer.Instance);
        var outcomes = new Dictionary<DependencyDocumentIdentity, Task<DependencyDocumentScanOutcome>>(DependencyDocumentIdentityComparer.Instance);
        var subscriptions = new List<NeedSlot>();
        var ownedBatches = new List<CollectionBatch>();
        NeedSlot[] quiescingSlots = [];
        Task<DependencyDocumentScanOutcome>[] quiescing = [];
        Task[] retirementWaits = [];

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposeStarted || shutdown.IsCancellationRequested, this);
            ExpireBucketsLocked(timeProvider.GetTimestamp());
            retirementWaits = targetRetirements
                .Where(retirement => string.Equals(retirement.TargetPath, targetPath, StringComparison.Ordinal)
                    && snapshotTicket <= retirement.MaximumTicket)
                .Select(retirement => retirement.Drained.Task)
                .ToArray();
            if (retirementWaits.Length == 0 && retainedBuckets.TryGetValue(bucketKey, out var bucket))
            {
                bucket.LastAccess = timeProvider.GetTimestamp();
                foreach (var item in plan.RequiredDocuments)
                    if (bucket.Facts.TryGetValue(item.Identity, out var fact))
                        facts[item.Identity] = fact;
            }

            if (retirementWaits.Length == 0)
            {
                quiescingSlots = plan.RequiredDocuments
                    .Where(item => !facts.ContainsKey(item.Identity))
                    .Select(item => inFlight.GetValueOrDefault(new NeedKey(bucketKey, item.Identity)))
                    .Where(slot => slot?.Quiescing == true || slot?.Batch?.Closing == true)
                    .Select(slot => slot!)
                    .Distinct()
                    .ToArray();
                quiescing = quiescingSlots.Select(slot => slot.Task).ToArray();
                if (quiescing.Length == 0)
                {
                    var newSlots = new List<NeedSlot>();
                    foreach (var item in plan.RequiredDocuments)
                    {
                        if (facts.ContainsKey(item.Identity)) continue;
                        var need = new NeedKey(bucketKey, item.Identity);
                        if (!inFlight.TryGetValue(need, out var slot))
                        {
                            slot = new NeedSlot(need, item);
                            inFlight.Add(need, slot);
                            newSlots.Add(slot);
                        }
                        slot.SubscriberCount++;
                        subscriptions.Add(slot);
                        outcomes[item.Identity] = slot.Task;
                    }

                    foreach (var batchSlots in newSlots.Chunk(DependencyGraphScanner.MaximumDocuments))
                    {
                        var batch = new CollectionBatch(bucketKey, plan, batchSlots, shutdown.Token);
                        foreach (var slot in batchSlots) slot.Batch = batch;
                        ownedBatches.Add(batch);
                        StartBatchLocked(batch);
                    }
                }
            }
        }

        if (retirementWaits.Length > 0)
        {
            await Task.WhenAll(retirementWaits).WaitAsync(requestToken).ConfigureAwait(false);
            return await CollectPlanAsync(plan, targetPath, snapshotTicket, requestToken).ConfigureAwait(false);
        }

        if (quiescing.Length > 0)
        {
            foreach (var slot in quiescingSlots) observer?.QuiescingWait?.Invoke(slot.Item.Document);
            await Task.WhenAll(quiescing).WaitAsync(requestToken).ConfigureAwait(false);
            return await CollectPlanAsync(plan, targetPath, snapshotTicket, requestToken).ConfigureAwait(false);
        }

        var retry = false;
        try
        {
            foreach (var slot in subscriptions) observer?.SubscriptionAdded?.Invoke(slot.Item.Document);
            var pending = outcomes.Select(pair => AwaitOutcomeAsync(pair.Key, pair.Value, requestToken)).ToArray();
            foreach (var (identity, outcome) in await Task.WhenAll(pending).ConfigureAwait(false))
            {
                if (outcome.Fact is not null) facts[identity] = outcome.Fact;
            }

            var orderedFacts = plan.RequiredDocuments
                .Select(item => facts.GetValueOrDefault(item.Identity))
                .Where(fact => fact is not null)
                .Cast<DependencyDocumentFact>()
                .ToArray();
            var errors = new List<DependencyGraphScanError>();
            foreach (var item in plan.RequiredDocuments)
            {
                if (!outcomes.TryGetValue(item.Identity, out var task)) continue;
                var result = await task.ConfigureAwait(false);
                errors.AddRange(result.Errors);
                if (result.Fact is null && result.Errors.IsEmpty && !requestToken.IsCancellationRequested)
                {
                    retry = true;
                    break;
                }
            }

            if (!retry)
            {
                requestToken.ThrowIfCancellationRequested();
                var newScanCount = ownedBatches.Sum(batch => Volatile.Read(ref batch.SemanticScanCount));
                return DependencyGraphScanner.CreateCollectionFromFacts(plan, orderedFacts, errors, newScanCount);
            }
        }
        finally
        {
            Unsubscribe(subscriptions);
        }

        if (retry)
            return await CollectPlanAsync(plan, targetPath, snapshotTicket, requestToken).ConfigureAwait(false);
        throw new InvalidOperationException("Dependency collection completed without a result.");
    }

    private static async Task<(DependencyDocumentIdentity Identity, DependencyDocumentScanOutcome Outcome)> AwaitOutcomeAsync(
        DependencyDocumentIdentity identity,
        Task<DependencyDocumentScanOutcome> outcome,
        CancellationToken cancellationToken) =>
        (identity, await outcome.WaitAsync(cancellationToken).ConfigureAwait(false));

    internal async Task RetireTargetAsync(string canonicalTargetPath, long maximumRetiredSnapshotTicket)
    {
        if (maximumRetiredSnapshotTicket <= 0 || string.IsNullOrWhiteSpace(canonicalTargetPath)) return;
        var target = CanonicalizeTarget(canonicalTargetPath);
        CollectionBatch[] jobs;
        var retirement = new TargetRetirement(target, maximumRetiredSnapshotTicket);
        lock (gate)
        {
            targetRetirements.Add(retirement);
            foreach (var key in retainedBuckets.Keys
                         .Where(key => string.Equals(key.TargetPath, target, StringComparison.Ordinal)
                             && key.SnapshotTicket <= maximumRetiredSnapshotTicket).ToArray())
                retainedBuckets.Remove(key);
            jobs = activeBatches.Values.Where(batch =>
                    string.Equals(batch.Key.TargetPath, target, StringComparison.Ordinal)
                    && batch.Key.SnapshotTicket <= maximumRetiredSnapshotTicket).ToArray();
            foreach (var job in jobs)
            {
                job.Retired = true;
                job.Closing = true;
            }
        }

        try
        {
            foreach (var job in jobs)
            {
                try { await job.Cancellation.CancelAsync().ConfigureAwait(false); }
                catch (ObjectDisposedException) { }
            }
            var running = jobs.Select(job => job.Task).Where(task => task is not null).Cast<Task>().ToArray();
            try { await Task.WhenAll(running).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        finally
        {
            lock (gate) targetRetirements.Remove(retirement);
            retirement.Drained.TrySetResult();
        }
    }

    private void StartBatchLocked(CollectionBatch batch)
    {
        var id = ++nextBatchId;
        activeBatches.Add(id, batch);
        batch.Task = Task.Run(() => RunBatchAsync(id, batch));
    }

    private async Task RunBatchAsync(long id, CollectionBatch batch)
    {
        var compilations = new Dictionary<ProjectId, Compilation?>();
        var completed = new HashSet<NeedSlot>();
        try
        {
            foreach (var slot in batch.Slots)
            {
                if (!HasSubscribers(slot))
                {
                    CompleteSlot(slot, new DependencyDocumentScanOutcome(null, ImmutableArray<DependencyGraphScanError>.Empty, false));
                    completed.Add(slot);
                    continue;
                }

                DependencyDocumentScanOutcome outcome;
                if (slot.Item.TextError is not null)
                {
                    outcome = new DependencyDocumentScanOutcome(null,
                        [new DependencyGraphScanError(slot.Item.Project.Name, slot.Item.Document.Name, slot.Item.TextError)], false);
                }
                else
                {
                    try
                    {
                        if (!compilations.TryGetValue(slot.Item.Project.Id, out var compilation))
                        {
                            compilation = await slot.Item.Project.GetCompilationAsync(batch.Cancellation.Token).ConfigureAwait(false);
                            compilations[slot.Item.Project.Id] = compilation;
                            observer?.CompilationAcquired?.Invoke(slot.Item.Project);
                        }

                        if (compilation is null)
                        {
                            outcome = new DependencyDocumentScanOutcome(null,
                                [new DependencyGraphScanError(slot.Item.Project.Name, slot.Item.Document.Name, "Compilation was unavailable.")], false);
                        }
                        else
                        {
                            Interlocked.Increment(ref batch.SemanticScanCount);
                            outcome = await DependencyGraphScanner.CollectDocumentFactAsync(
                                batch.Plan.Solution, batch.Plan, slot.Item, compilation,
                                observer, batch.Cancellation.Token).ConfigureAwait(false);
                        }
                    }
                    catch (OperationCanceledException) when (batch.Cancellation.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        outcome = new DependencyDocumentScanOutcome(null,
                            [new DependencyGraphScanError(slot.Item.Project.Name, slot.Item.Document.Name,
                                $"Dependency collection failed: {exception.Message}")], false);
                    }
                }

                CompleteSlot(slot, outcome);
                completed.Add(slot);
            }
        }
        catch (OperationCanceledException) when (batch.Cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var slot in batch.Slots.Where(slot => !completed.Contains(slot)))
            {
                var outcome = batch.Retired
                    ? new DependencyDocumentScanOutcome(null,
                        [new DependencyGraphScanError(slot.Item.Project.Name, slot.Item.Document.Name,
                            "Source owner retired during dependency collection.")], false)
                    : new DependencyDocumentScanOutcome(null, ImmutableArray<DependencyGraphScanError>.Empty, false);
                CompleteSlot(slot, outcome);
            }
            lock (gate)
            {
                batch.Closing = true;
                activeBatches.Remove(id);
            }
            batch.Cancellation.Dispose();
        }
    }

    private bool HasSubscribers(NeedSlot slot)
    {
        lock (gate) return slot.SubscriberCount > 0 && !slot.Batch!.Retired;
    }

    private void CompleteSlot(NeedSlot slot, DependencyDocumentScanOutcome outcome)
    {
        lock (gate)
        {
            var batch = slot.Batch!;
            if (outcome.Fact is { } fact && slot.SubscriberCount > 0 && !batch.Retired && !shutdown.IsCancellationRequested)
                TryAdmitFactLocked(batch.Key, fact, timeProvider.GetTimestamp());
            else if (slot.SubscriberCount == 0 || batch.Retired)
                outcome = new DependencyDocumentScanOutcome(null,
                    batch.Retired && slot.SubscriberCount > 0
                        ? [new DependencyGraphScanError(slot.Item.Project.Name, slot.Item.Document.Name,
                            "Source owner retired during dependency collection.")]
                        : ImmutableArray<DependencyGraphScanError>.Empty,
                    outcome.SemanticScanAttempted);

            inFlight.Remove(slot.Key);
            slot.Completion.TrySetResult(outcome);
        }
    }

    private void Unsubscribe(IReadOnlyList<NeedSlot> subscriptions)
    {
        var cancel = new HashSet<CollectionBatch>();
        lock (gate)
        {
            foreach (var slot in subscriptions)
            {
                if (slot.SubscriberCount > 0) slot.SubscriberCount--;
                var batch = slot.Batch;
                if (slot.SubscriberCount == 0 && !slot.Task.IsCompleted) slot.Quiescing = true;
                if (batch is null || batch.Closing || batch.Slots.Any(candidate => candidate.SubscriberCount > 0)) continue;
                batch.Closing = true;
                cancel.Add(batch);
            }
        }
        foreach (var batch in cancel) _ = CancelBatchSafelyAsync(batch);
    }

    private static async Task CancelBatchSafelyAsync(CollectionBatch batch)
    {
        try { await batch.Cancellation.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { }
    }

    private void TryAdmitFactLocked(BucketKey key, DependencyDocumentFact fact, long now)
    {
        ExpireBucketsLocked(now);
        var existing = retainedBuckets.GetValueOrDefault(key);
        if (existing?.Facts.ContainsKey(fact.Identity) == true)
        {
            existing.LastAccess = now;
            return;
        }

        var standaloneBytes = EstimatePayload(key, [fact]);
        if (standaloneBytes is null || standaloneBytes > maximumRetainedBytes || maximumBuckets == 0) return;

        var candidateFacts = existing is null
            ? new List<DependencyDocumentFact> { fact }
            : existing.Facts.Values.Append(fact).ToList();
        var candidateBytes = EstimatePayload(key, candidateFacts);
        if (candidateBytes is null) return;

        var candidate = new Bucket(key, now);
        _ = ShareString(key.TargetPath, candidate.Strings);
        foreach (var candidateFact in candidateFacts)
        {
            var shared = ShareStrings(candidateFact, candidate.Strings);
            candidate.Facts.Add(shared.Identity, shared);
        }
        candidate.PayloadBytes = candidateBytes.Value;

        while (true)
        {
            var retainedCount = retainedBuckets.Count - (existing is null ? 0 : 1) + 1;
            var retainedBytes = retainedBuckets.Values.Where(bucket => bucket.Key != key).Sum(bucket => bucket.PayloadBytes)
                + candidate.PayloadBytes;
            if (retainedCount <= maximumBuckets && retainedBytes <= maximumRetainedBytes) break;

            var oldest = retainedBuckets.Values.Append(candidate).Aggregate((left, right) => CompareBucket(left, right) <= 0 ? left : right);
            if (ReferenceEquals(oldest, candidate)) return;
            retainedBuckets.Remove(oldest.Key);
            if (oldest.Key == key)
            {
                existing = null;
                candidate = new Bucket(key, now);
                _ = ShareString(key.TargetPath, candidate.Strings);
                var shared = ShareStrings(fact, candidate.Strings);
                candidate.Facts.Add(shared.Identity, shared);
                candidate.PayloadBytes = standaloneBytes.Value;
            }
        }

        retainedBuckets[key] = candidate;
    }

    private void ExpireBucketsLocked(long now)
    {
        foreach (var bucket in retainedBuckets.Values
                     .Where(bucket => timeProvider.GetElapsedTime(bucket.LastAccess, now) >= idleTtl)
                     .ToArray())
            retainedBuckets.Remove(bucket.Key);
    }

    private static int CompareBucket(Bucket left, Bucket right)
    {
        var comparison = left.LastAccess.CompareTo(right.LastAccess);
        if (comparison != 0) return comparison;
        comparison = StringComparer.Ordinal.Compare(left.Key.TargetPath, right.Key.TargetPath);
        if (comparison != 0) return comparison;
        comparison = left.Key.SnapshotTicket.CompareTo(right.Key.SnapshotTicket);
        if (comparison != 0) return comparison;
        comparison = ((int)left.Key.ScopeType).CompareTo((int)right.Key.ScopeType);
        if (comparison != 0) return comparison;
        return left.Key.IncludeGenerated.CompareTo(right.Key.IncludeGenerated);
    }

    private static long? EstimatePayload(BucketKey key, IReadOnlyList<DependencyDocumentFact> facts)
    {
        try
        {
            var strings = new HashSet<string>(StringComparer.Ordinal) { key.TargetPath };
            foreach (var fact in facts)
                foreach (var value in EnumerateStrings(fact))
                    strings.Add(value);
            long stringBytes = 0;
            var encoding = new UTF8Encoding(false, true);
            foreach (var value in strings) stringBytes += encoding.GetByteCount(value);
            // One 64-byte record is the successful coverage entry represented by each fact;
            // every retained edge has its own separately charged 64-byte record.
            var records = facts.Count + facts.Sum(fact => (long)fact.TypeDependencies.Length);
            return stringBytes + 17 + records * 64;
        }
        catch (EncoderFallbackException)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateStrings(DependencyDocumentFact fact)
    {
        var identity = fact.Identity;
        yield return identity.OwnerProjectPath;
        yield return identity.OwnerContextFingerprint;
        yield return identity.DocumentPath;
        yield return identity.Name;
        foreach (var folder in identity.Folders) yield return folder;
        yield return identity.SourceCodeKind;
        yield return identity.TextHash;
        foreach (var edge in fact.TypeDependencies)
        {
            yield return edge.FromTypeId;
            yield return edge.ToTypeId;
            yield return edge.FromType;
            yield return edge.ToType;
            yield return edge.FromTypeName;
            yield return edge.ToTypeName;
            yield return edge.FromNamespace;
            yield return edge.ToNamespace;
            yield return edge.FromProject;
            yield return edge.ToProject;
            yield return edge.FromFile;
            yield return edge.ToFile;
            if (edge.FromHandoffId is { } fromHandoffId) yield return fromHandoffId;
            if (edge.ToHandoffId is { } toHandoffId) yield return toHandoffId;
        }
        foreach (var declaration in fact.TypeDeclarations)
        {
            yield return declaration.TypeId;
            yield return declaration.DisplayName;
            yield return declaration.Name;
            yield return declaration.Namespace;
            yield return declaration.Project;
            yield return declaration.File;
            yield return declaration.OwnerProjectPath;
            yield return declaration.OwnerContextFingerprint;
            yield return declaration.OriginalTypeId;
            yield return declaration.DocumentationCommentId;
            foreach (var document in declaration.DeclarationDocuments)
            {
                yield return document.OwnerProjectPath;
                yield return document.OwnerContextFingerprint;
                yield return document.DocumentPath;
                yield return document.Name;
                foreach (var folder in document.Folders) yield return folder;
                yield return document.SourceCodeKind;
                yield return document.TextHash;
            }
        }
    }

    private static DependencyDocumentFact ShareStrings(DependencyDocumentFact fact, Dictionary<string, string> pool)
    {
        DependencyDocumentIdentity ShareIdentity(DependencyDocumentIdentity identity) => identity with
        {
            OwnerProjectPath = ShareString(identity.OwnerProjectPath, pool),
            OwnerContextFingerprint = ShareString(identity.OwnerContextFingerprint, pool),
            DocumentPath = ShareString(identity.DocumentPath, pool),
            Name = ShareString(identity.Name, pool),
            Folders = identity.Folders.Select(value => ShareString(value, pool)).ToImmutableArray(),
            SourceCodeKind = ShareString(identity.SourceCodeKind, pool),
            TextHash = ShareString(identity.TextHash, pool),
        };
        return fact with
        {
            Identity = ShareIdentity(fact.Identity),
            TypeDependencies = fact.TypeDependencies.Select(edge => edge with
            {
                FromTypeId = ShareString(edge.FromTypeId, pool),
                ToTypeId = ShareString(edge.ToTypeId, pool),
                FromType = ShareString(edge.FromType, pool),
                ToType = ShareString(edge.ToType, pool),
                FromTypeName = ShareString(edge.FromTypeName, pool),
                ToTypeName = ShareString(edge.ToTypeName, pool),
                FromNamespace = ShareString(edge.FromNamespace, pool),
                ToNamespace = ShareString(edge.ToNamespace, pool),
                FromProject = ShareString(edge.FromProject, pool),
                ToProject = ShareString(edge.ToProject, pool),
                FromFile = ShareString(edge.FromFile, pool),
                ToFile = ShareString(edge.ToFile, pool),
                FromHandoffId = edge.FromHandoffId is null ? null : ShareString(edge.FromHandoffId, pool),
                ToHandoffId = edge.ToHandoffId is null ? null : ShareString(edge.ToHandoffId, pool),
            }).ToImmutableArray(),
            TypeDeclarations = fact.TypeDeclarations.Select(declaration => declaration with
            {
                TypeId = ShareString(declaration.TypeId, pool),
                DisplayName = ShareString(declaration.DisplayName, pool),
                Name = ShareString(declaration.Name, pool),
                Namespace = ShareString(declaration.Namespace, pool),
                Project = ShareString(declaration.Project, pool),
                File = ShareString(declaration.File, pool),
                OwnerProjectPath = ShareString(declaration.OwnerProjectPath, pool),
                OwnerContextFingerprint = ShareString(declaration.OwnerContextFingerprint, pool),
                OriginalTypeId = ShareString(declaration.OriginalTypeId, pool),
                DocumentationCommentId = ShareString(declaration.DocumentationCommentId, pool),
                DeclarationDocuments = declaration.DeclarationDocuments.Select(ShareIdentity).ToImmutableArray(),
            }).ToImmutableArray(),
        };
    }

    private static string ShareString(string value, Dictionary<string, string> pool)
    {
        if (pool.TryGetValue(value, out var shared)) return shared;
        pool.Add(value, value);
        return value;
    }

    internal int RetainedBucketCount
    {
        get { lock (gate) return retainedBuckets.Count; }
    }

    internal long RetainedPayloadBytes
    {
        get { lock (gate) return retainedBuckets.Values.Sum(bucket => bucket.PayloadBytes); }
    }

    internal ImmutableArray<DependencyDocumentFact> SnapshotRetainedFactsForTesting(
        string targetPath,
        long snapshotTicket,
        SymbolScopeType scopeType,
        bool includeGenerated)
    {
        lock (gate)
        {
            var key = new BucketKey(CanonicalizeTarget(targetPath), snapshotTicket, scopeType, includeGenerated);
            return retainedBuckets.TryGetValue(key, out var bucket) ? bucket.Facts.Values.ToImmutableArray() : [];
        }
    }

    private static string CanonicalizeTarget(string path)
    {
        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            throw new ArgumentException("The dependency cache target path is invalid.", nameof(path), exception);
        }
        return AnalysisPathIdentity.TryNormalize(fullPath, out var canonical) ? canonical : fullPath.Replace('\\', '/');
    }

    public async ValueTask DisposeAsync()
    {
        CollectionBatch[] jobs;
        var ownsDisposal = false;
        lock (gate)
        {
            if (disposeStarted)
            {
                jobs = [];
            }
            else
            {
                ownsDisposal = true;
                disposeStarted = true;
                jobs = activeBatches.Values.ToArray();
                foreach (var job in jobs)
                {
                    job.Retired = true;
                    job.Closing = true;
                }
                retainedBuckets.Clear();
            }
        }
        if (!ownsDisposal)
        {
            await disposeCompletion.Task.ConfigureAwait(false);
            return;
        }

        try
        {
            await shutdown.CancelAsync().ConfigureAwait(false);
            foreach (var job in jobs) await CancelBatchSafelyAsync(job).ConfigureAwait(false);
            var running = jobs.Select(job => job.Task).Where(task => task is not null).Cast<Task>().ToArray();
            try { await Task.WhenAll(running).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            shutdown.Dispose();
            disposeCompletion.TrySetResult();
        }
        catch (Exception exception)
        {
            disposeCompletion.TrySetException(exception);
            throw;
        }
    }
}
