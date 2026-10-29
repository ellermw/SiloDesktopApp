using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using SiloPlayer.Core.Models.Updates;
using SiloPlayer.Core.Services.Updates;

namespace SiloPlayer.Tests;

public sealed class UpdateTransactionLifecycleTests : IDisposable
{
    private const string OldVersion = "1.2.464.0", NewVersion = "1.2.465.0";
    private readonly string sandbox = Path.Combine(Path.GetTempPath(), "silo-transaction-" + Guid.NewGuid().ToString("N"));
    private string Root => Path.Combine(sandbox, "installation");
    private readonly List<EnvironmentAdapter> environments = [];

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UacDenialOrStaleParentCannotReachAuthorityOrMutateInstallation(bool denied)
    {
        using var env = Create();
        var before = File.ReadAllBytes(Path.Combine(Root, ".silo-update", "state.json"));
        env.ValidateError = denied ? new Win32Exception(1223) : new InvalidDataException("Stale parent identity");
        var result = Run(env);
        Assert.False(result.Succeeded); Assert.False(result.Activated);
        Assert.Equal(0, env.AuthorityCalls); Assert.Equal(0, env.LockAcquisitions);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(Root, ".silo-update", "state.json")));
        Assert.True(env.ParentAlive); AssertOldActive();
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("staged")]
    [InlineData("disconnected")]
    public void MissingVisibleBrokerAcknowledgementDoesNotStage(string kind)
    {
        using var env = Create(); env.VisibleKind = kind;
        Assert.False(Run(env).Succeeded);
        Assert.Equal(0, env.AuthorityCalls);
        Assert.False(Directory.Exists(Path.Combine(Root, "versions", NewVersion)));
        AssertOldActive(); Assert.True(env.ParentAlive);
    }

    [Fact]
    public void FullyInstalledTargetAndVisibleHandoffPrecedeAnyParentExitOrActivation()
    {
        using var env = Create(); env.WaitReturnsWhileAlive = true;
        env.OnStaged = () =>
        {
            Assert.True(env.ParentAlive);
            AssertOldActive();
            Assert.True(File.Exists(Path.Combine(Root, "versions", NewVersion, "SiloPlayer.dll")));
            Assert.Equal(File.ReadAllBytes(Path.Combine(env.Package.Directory, "client", "SiloPlayer.dll")),
                File.ReadAllBytes(Path.Combine(Root, "versions", NewVersion, "SiloPlayer.dll")));
            Assert.Equal("visible", env.ReceivedKinds[0]);
        };
        var result = Run(env);
        Assert.False(result.Succeeded); Assert.False(result.Activated);
        Assert.True(env.StagedObserved, result.Error?.ToString()); Assert.True(env.ParentAlive);
        AssertOldActive();
        Assert.DoesNotContain(env.Sent, message => message.Kind == "activated");
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("disconnected")]
    [InlineData("wrong-transaction")]
    [InlineData("canceled-token")]
    public void MissingNormalCloseOrHelperDeathPreservesLiveParentAndCanRetrySameTarget(string failure)
    {
        using var env = Create(); env.CloseFailure = failure;
        using var cancellation = new CancellationTokenSource();
        if (failure == "canceled-token") env.OnStaged = cancellation.Cancel;
        var result = Run(env, cancellation.Token);
        Assert.False(result.Succeeded); Assert.False(result.Activated);
        Assert.True(env.ParentAlive); AssertOldActive();
        Assert.False(Directory.Exists(Path.Combine(Root, "versions", NewVersion)));
        // The retry reuses the same target version, after real journal and
        // inventory cleanup, without needing to delete a populated target.
        env.ResetForRetry();
        var retried = Run(env, restart: false);
        Assert.True(retried.Succeeded); Assert.True(retried.Activated);
        Assert.Equal(NewVersion, SideBySideInstallation.ReadState(Root)!.ActiveVersion);
    }

    [Fact]
    public void ManualUpdateReleasesRootGateBeforeRestartAndConfirmsExactLiveReadyProcess()
    {
        using var env = Create();
        var thread = Environment.CurrentManagedThreadId;
        var result = Run(env);
        Assert.True(result.Succeeded, result.Error?.ToString()); Assert.True(result.Activated); Assert.True(result.Confirmed);
        Assert.False(env.ParentAlive); Assert.True(env.NewClientAlive);
        Assert.Equal(2, env.LockAcquisitions);
        Assert.All(env.LockThreadIds, id => Assert.Equal(thread, id));
        Assert.Equal(0, env.HeldLocks);
        Assert.Equal(new[] { "visible", "close", "ready" }, env.ReceivedKinds);
        Assert.Equal(new[] { "staged", "activated", "confirmed" },
            env.Sent.Where(message => message.Kind is "staged" or "activated" or "confirmed").Select(message => message.Kind));
        var state = SideBySideInstallation.ReadState(Root)!;
        Assert.Equal(NewVersion, state.ActiveVersion); Assert.Equal(OldVersion, state.PreviousVersion);
        Assert.Null(state.PendingVersion); Assert.Null(state.TransactionId);
        Assert.True(File.Exists(Path.Combine(Root, "versions", OldVersion, "SiloPlayer.exe")));
    }

    [Fact]
    public void AutomaticNormalExitSelectsPendingVersionWithoutLaunchingOrAwaitingReady()
    {
        using var env = Create(); env.LaunchNewClient = false;
        var result = Run(env, restart: false);
        Assert.True(result.Succeeded); Assert.True(result.Activated); Assert.False(result.Confirmed);
        Assert.False(env.ParentAlive); Assert.False(env.NewClientAlive);
        Assert.Equal(new[] { "visible", "close" }, env.ReceivedKinds);
        Assert.Equal(1, env.LockAcquisitions);
        Assert.Equal(NewVersion, SideBySideInstallation.ReadState(Root)!.PendingVersion);
        Assert.DoesNotContain(env.Sent, message => message.Kind == "confirmed");
    }

    [Fact]
    public void LostConfirmationAcknowledgementDoesNotUndoVerifiedStartup()
    {
        using var env = Create(); env.LoseConfirmation = true;
        var result = Run(env);
        Assert.False(result.Succeeded); Assert.True(result.Activated); Assert.True(result.Confirmed);
        Assert.True(env.NewClientAlive);
        Assert.Equal(NewVersion, SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        Assert.Null(SideBySideInstallation.ReadState(Root)!.PendingVersion);
    }

    [Fact]
    public void InteractiveCurrentVersionReconcilesPriorAutomaticPendingBeforeNextStage()
    {
        using var first = Create(); first.LaunchNewClient = false;
        Assert.True(Run(first, restart: false).Succeeded);
        Assert.Equal(NewVersion, SideBySideInstallation.ReadState(Root)!.PendingVersion);
        using var next = new EnvironmentAdapter(Root, Package("1.2.466.0"), NewVersion);
        environments.Add(next); next.LaunchNewClient = false;
        next.OnVisible = () => Assert.Null(SideBySideInstallation.ReadState(Root)!.PendingVersion);
        var result = UpdateTransactionRunner.Run(new(Root, NewVersion, "v1.2.466", false), next, default);
        Assert.True(result.Succeeded, result.Error?.ToString());
        var state = SideBySideInstallation.ReadState(Root)!;
        Assert.Equal("1.2.466.0", state.ActiveVersion); Assert.Equal("1.2.466.0", state.PendingVersion);
        Assert.Equal(NewVersion, state.PreviousVersion);
    }

    [Fact]
    public void AuthenticatedPreviousClientCanRecoverFailedPendingAndRetryTheSameVersion()
    {
        using var first = Create(); first.LaunchNewClient = false;
        Assert.True(Run(first, restart: false).Succeeded);
        var failedTransaction = SideBySideInstallation.ReadState(Root)!.TransactionId;
        using var fallback = new EnvironmentAdapter(Root, Package(NewVersion));
        environments.Add(fallback); fallback.LaunchNewClient = false;
        fallback.OnVisible = () =>
        {
            AssertOldActive();
            Assert.False(Directory.Exists(Path.Combine(Root, "versions", NewVersion)));
        };
        var result = Run(fallback, restart: false);
        Assert.True(result.Succeeded, result.Error?.ToString());
        Assert.NotEqual(failedTransaction, result.TransactionId);
        var state = SideBySideInstallation.ReadState(Root)!;
        Assert.Equal(NewVersion, state.ActiveVersion); Assert.Equal(NewVersion, state.PendingVersion);
        Assert.Equal(OldVersion, state.PreviousVersion);
    }

    [Fact]
    public void AuthenticatedOlderClientCannotRollBackAnAlreadyConfirmedVersion()
    {
        using var first = Create(); first.LaunchNewClient = false;
        var installed = Run(first, restart: false);
        Assert.True(installed.Succeeded);
        SideBySideInstallation.Confirm(Root, installed.TransactionId!);
        using var old = new EnvironmentAdapter(Root, Package("1.2.466.0")); environments.Add(old);
        var result = UpdateTransactionRunner.Run(new(Root, OldVersion, "v1.2.466", false), old, default);
        Assert.False(result.Succeeded); Assert.Equal(0, old.AuthorityCalls);
        Assert.True(old.ParentAlive); Assert.Equal(NewVersion, SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        Assert.Null(SideBySideInstallation.ReadState(Root)!.PendingVersion);
    }

    [Fact]
    public void PreviousClientCannotRollBackALivePendingCandidateButCanRecoverAfterItExits()
    {
        using var first = Create();
        Assert.True(Run(first, restart: false).Succeeded);
        Assert.True(first.NewClientAlive);
        var before = File.ReadAllBytes(Path.Combine(Root, ".silo-update", "state.json"));
        using var fallback = new EnvironmentAdapter(Root, Package("1.2.466.0")); environments.Add(fallback);
        fallback.AdditionalVersionUse = directory => first.NewClientAlive &&
            directory == Path.Combine(Root, "versions", NewVersion);
        var request = new UpdateTransactionRequest(Root, OldVersion, "v1.2.466", false);
        var blocked = UpdateTransactionRunner.Run(request, fallback, default);
        Assert.False(blocked.Succeeded); Assert.Equal(0, fallback.AuthorityCalls);
        Assert.True(first.NewClientAlive); Assert.True(fallback.ParentAlive);
        Assert.Equal(before, File.ReadAllBytes(Path.Combine(Root, ".silo-update", "state.json")));
        first.StopNewClient();
        fallback.ResetForRetry();
        var recovered = UpdateTransactionRunner.Run(request, fallback, default);
        Assert.True(recovered.Succeeded, recovered.Error?.ToString());
        Assert.Equal("1.2.466.0", SideBySideInstallation.ReadState(Root)!.ActiveVersion);
        Assert.Equal(OldVersion, SideBySideInstallation.ReadState(Root)!.PreviousVersion);
    }

    [Theory]
    [InlineData("free")]
    [InlineData("unknown")]
    [InlineData("failure")]
    [InlineData("retirement-failure")]
    public void ConfirmedUpdateRetainsActivePreviousAndForeignFilesWhileCleanupCanDefer(string inventory)
    {
        using var first = Create(); first.LaunchNewClient = false;
        var foreign = Path.Combine(Root, "versions", OldVersion, "keep.txt");
        File.WriteAllText(foreign, "synthetic foreign addition");
        Assert.True(Run(first, restart: false).Succeeded);
        using var next = new EnvironmentAdapter(Root, Package("1.2.466.0"), NewVersion);
        environments.Add(next); next.CleanupInventory = inventory;
        var result = UpdateTransactionRunner.Run(new(Root, NewVersion, "v1.2.466", true), next, default);
        Assert.True(result.Succeeded, result.Error?.ToString()); Assert.True(result.Confirmed);
        Assert.Equal("synthetic foreign addition", File.ReadAllText(foreign));
        Assert.Equal(inventory != "free", File.Exists(Path.Combine(Root, "versions", OldVersion, "SiloPlayer.exe")));
        Assert.Equal(inventory is "free" or "retirement-failure" ? new[] { Path.Combine(Root, "versions", OldVersion) } : [], next.RetiredDirectories);
        Assert.True(File.Exists(Path.Combine(Root, "versions", NewVersion, "SiloPlayer.exe")));
        Assert.True(File.Exists(Path.Combine(Root, "versions", "1.2.466.0", "SiloPlayer.exe")));
        var state = SideBySideInstallation.ReadState(Root)!;
        Assert.Equal("1.2.466.0", state.ActiveVersion); Assert.Equal(NewVersion, state.PreviousVersion);
        Assert.Null(state.PendingVersion);
    }

    [Theory]
    [InlineData("wrong-start")]
    [InlineData("wrong-transaction")]
    [InlineData("disconnected")]
    public void WrongReadyIdentityOrHelperDeathKeepsPendingRecoveryWithoutKillingNewClient(string failure)
    {
        using var env = Create(); env.ReadyFailure = failure;
        var result = Run(env);
        Assert.False(result.Succeeded); Assert.True(result.Activated); Assert.False(result.Confirmed);
        Assert.True(env.NewClientAlive);
        var state = SideBySideInstallation.ReadState(Root)!;
        Assert.Equal(NewVersion, state.ActiveVersion); Assert.Equal(NewVersion, state.PendingVersion);
        Assert.Equal(OldVersion, state.PreviousVersion);
        Assert.True(File.Exists(Path.Combine(Root, "versions", OldVersion, "SiloPlayer.exe")));
        Assert.DoesNotContain(env.Sent, message => message.Kind == "confirmed");
    }

    [Theory]
    [InlineData("authority")]
    [InlineData("poisoned-cache")]
    [InlineData("firewall")]
    public void AuthorityCacheOrPlatformFailureNeverSelectsUnverifiedTarget(string failure)
    {
        using var env = Create(); env.PreparationFailure = failure;
        var result = Run(env);
        Assert.False(result.Succeeded); Assert.False(result.Activated);
        Assert.True(env.ParentAlive); AssertOldActive();
        Assert.DoesNotContain(env.Sent, message => message.Kind == "activated");
    }

    [Fact]
    public void AnotherExactSyntheticClientBlocksActivationWithoutTerminatingThatClient()
    {
        using var env = Create(); env.StartOtherClient();
        var result = Run(env);
        Assert.False(result.Succeeded); Assert.False(result.Activated);
        Assert.True(env.OtherClientAlive); AssertOldActive();
    }

    [Fact]
    public void CompetingInstallationMutexPreventsAuthorityAndMutation()
    {
        using var env = Create();
        using var acquired = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var holder = new Thread(() =>
        {
            using var gate = new Mutex(false, SideBySideInstallation.GetMutexName(Root));
            gate.WaitOne(); acquired.Set(); release.Wait(); gate.ReleaseMutex();
        });
        holder.Start(); acquired.Wait();
        try
        {
            Assert.False(Run(env).Succeeded);
            Assert.Equal(0, env.AuthorityCalls); AssertOldActive(); Assert.True(env.ParentAlive);
        }
        finally { release.Set(); holder.Join(); }
    }

    [Fact]
    public void AuthorityRateLimitSurvivesNewWorkerRequestsUntilTheExactAbsoluteDeadline()
    {
        using var first = Create(); first.PreparationFailure = "rate-limit";
        var clock = new AuthorityClock();
        var request = new UpdateTransactionRequest(Root, OldVersion, "v1.2.465", true);
        var result = UpdateTransactionRunner.Run(request, first, default, clock);
        Assert.False(result.Succeeded); Assert.True(first.ParentAlive);
        var deadline = clock.GetUtcNow().AddMinutes(45);
        Assert.Equal(deadline, first.Sent.Last(message => message.Kind == "failed").CooldownUntil);
        Assert.Equal(1, first.AuthorityCalls); Assert.Equal(1, first.AuthorityRequests);
        var store = Path.Combine(Root, ".silo-update", "github-cooldown.json");
        var original = File.ReadAllBytes(store);
        clock.Advance(TimeSpan.FromMinutes(15));
        // A new adapter models a new finite worker with no prior in-memory state.
        using var fresh = new EnvironmentAdapter(Root, first.Package); environments.Add(fresh);
        fresh.PreparationFailure = "rate-limit";
        foreach (var advance in new[] { TimeSpan.Zero, TimeSpan.FromMinutes(29) + TimeSpan.FromSeconds(59) })
        {
            clock.Advance(advance); fresh.ResetForRetry();
            Assert.False(UpdateTransactionRunner.Run(request, fresh, default, clock).Succeeded);
            Assert.Equal(0, fresh.AuthorityCalls); Assert.Equal(0, fresh.AuthorityRequests);
            Assert.Equal(deadline, fresh.Sent.Last(message => message.Kind == "failed").CooldownUntil);
            Assert.Equal(original, File.ReadAllBytes(store));
            Assert.True(fresh.ParentAlive); AssertOldActive();
        }
        clock.Advance(TimeSpan.FromSeconds(1)); fresh.ResetForRetry();
        Assert.Equal(deadline, clock.GetUtcNow());
        Assert.False(UpdateTransactionRunner.Run(request, fresh, default, clock).Succeeded);
        Assert.Equal(1, fresh.AuthorityCalls); Assert.Equal(1, fresh.AuthorityRequests);
        Assert.Equal(clock.GetUtcNow().AddMinutes(45), UpdateAuthorityCooldown.Read(Root, clock));
    }

    private sealed class AuthorityClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }

    private UpdateTransactionResult Run(EnvironmentAdapter env, CancellationToken ct = default, bool restart = true) =>
        UpdateTransactionRunner.Run(new(Root, OldVersion, "v1.2.465", restart), env, ct);
    private void AssertOldActive()
    {
        var state = SideBySideInstallation.ReadState(Root)!;
        Assert.Equal(OldVersion, state.ActiveVersion); Assert.Null(state.PendingVersion);
    }
    private EnvironmentAdapter Create()
    {
        Directory.CreateDirectory(Root);
        var old = Package(OldVersion);
        var oldDirectory = Path.Combine(Root, "versions", OldVersion);
        Directory.CreateDirectory(oldDirectory);
        foreach (var entry in old.Manifest.Files)
        {
            var destination = Path.Combine(oldDirectory, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Path.Combine(old.Directory, "client", entry.Path.Replace('/', Path.DirectorySeparatorChar)), destination);
        }
        SideBySideInstallation.Initialize(Root, old.Manifest);
        var env = new EnvironmentAdapter(Root, Package(NewVersion)); environments.Add(env); return env;
    }
    private PreparedUpdatePackage Package(string version)
    {
        var directory = Path.Combine(sandbox, "package-" + version);
        Directory.CreateDirectory(Path.Combine(directory, "client", "Updater"));
        var entries = new List<UpdateFileEntry>();
        var values = new Dictionary<string, byte[]>
        {
            ["SiloPlayer.exe"] = File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "ping.exe")),
            ["SiloPlayer.dll"] = System.Text.Encoding.UTF8.GetBytes("synthetic client " + version),
            ["Updater/SiloPlayer.UpdateHost.exe"] = System.Text.Encoding.UTF8.GetBytes("synthetic host " + version)
        };
        foreach (var (path, bytes) in values)
        {
            File.WriteAllBytes(Path.Combine(directory, "client", path.Replace('/', Path.DirectorySeparatorChar)), bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            entries.Add(new(path, bytes.Length, hash, entries.Count, 1, hash));
        }
        var manifest = new UpdateManifest(1, version, entries.Count, new string('A', 64), entries);
        var pathToManifest = Path.Combine(directory, "manifest.json");
        File.WriteAllBytes(pathToManifest, JsonSerializer.SerializeToUtf8Bytes(manifest));
        return new(directory, pathToManifest, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pathToManifest))), manifest);
    }
    public void Dispose()
    {
        foreach (var env in environments) env.Dispose();
        var full = Path.GetFullPath(sandbox);
        if (!full.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(full) != Path.GetFileName(sandbox) || !Path.GetFileName(full).StartsWith("silo-transaction-", StringComparison.Ordinal))
            throw new InvalidOperationException("Cleanup escaped the synthetic transaction sandbox");
        var deadline = Stopwatch.StartNew();
        while (Directory.Exists(full))
        {
            try { Directory.Delete(full, true); }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && deadline.Elapsed < TimeSpan.FromSeconds(3))
            { Thread.Sleep(20); }
        }
    }

    private sealed class EnvironmentAdapter : IUpdateTransactionEnvironment, IDisposable
    {
        private readonly string root;
        private readonly string sourceVersion;
        public PreparedUpdatePackage Package { get; }
        private readonly Process parent;
        private readonly long parentStart;
        private Process? next, other;
        private int reads;
        private bool disposed;
        private string? transaction;
        public Exception? ValidateError;
        public string VisibleKind = "visible", CloseFailure = "", ReadyFailure = "", PreparationFailure = "";
        public string CleanupInventory = "free";
        public bool WaitReturnsWhileAlive, LaunchNewClient = true, LoseConfirmation;
        public Action? OnStaged, OnVisible;
        public Func<string, bool>? AdditionalVersionUse;
        public bool StagedObserved;
        public int AuthorityCalls, AuthorityRequests, LockAcquisitions, HeldLocks;
        public List<int> LockThreadIds { get; } = [];
        public List<UpdateHostMessage> Sent { get; } = [];
        public List<string> ReceivedKinds { get; } = [];
        public List<string> RetiredDirectories { get; } = [];
        public bool ParentAlive => !parent.HasExited;
        public bool NewClientAlive => next is not null && !next.HasExited;
        public bool OtherClientAlive => other is not null && !other.HasExited;
        public bool HasParentExited => parent.HasExited;
        public EnvironmentAdapter(string root, PreparedUpdatePackage package, string sourceVersion = OldVersion)
        {
            this.root = root; this.sourceVersion = sourceVersion; Package = package;
            parent = Start(Path.Combine(root, "versions", sourceVersion, "SiloPlayer.exe"));
            parentStart = parent.StartTime.ToUniversalTime().Ticks;
        }
        public void ValidateCallerAndParent()
        {
            if (ValidateError is not null) throw ValidateError;
            if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != parentStart ||
                !string.Equals(parent.MainModule!.FileName, Path.Combine(root, "versions", sourceVersion, "SiloPlayer.exe"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Wrong parent identity");
        }
        public IDisposable AcquireInstallationLock(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var gate = new Mutex(false, SideBySideInstallation.GetMutexName(root));
            if (!gate.WaitOne(TimeSpan.FromMilliseconds(100))) { gate.Dispose(); throw new IOException("Another synthetic updater owns the gate"); }
            HeldLocks++; LockAcquisitions++; LockThreadIds.Add(Environment.CurrentManagedThreadId);
            return new Lease(() => { LockThreadIds.Add(Environment.CurrentManagedThreadId); HeldLocks--; gate.ReleaseMutex(); gate.Dispose(); });
        }
        public PreparedUpdatePackage FetchAuthoritativePackage(string tag, CancellationToken ct)
        {
            Assert.Equal("v" + Version.Parse(Package.Manifest.Version).ToString(3), tag); Assert.Equal(1, HeldLocks); AuthorityCalls++;
            if (PreparationFailure == "rate-limit")
            {
                using var http = new HttpClient(new AuthorityRateLimitHandler(() => AuthorityRequests++));
                var response = new IncrementalReleaseAuthority(http).FetchAsync(tag, Path.Combine(root, ".silo-update", "unwritten-authority"), ct).GetAwaiter().GetResult();
                return new(Package.Directory, response.ManifestPath, response.Release.Incremental!.ManifestSha256, response.Manifest);
            }
            if (PreparationFailure == "authority") throw new HttpRequestException("Synthetic release authority failure");
            if (PreparationFailure == "poisoned-cache") File.WriteAllText(Path.Combine(Package.Directory, "client", "SiloPlayer.dll"), "poisoned bytes");
            return Package;
        }
        public void AuthorizeNewClient(string executable)
        {
            Assert.True(File.Exists(executable)); Assert.True(ParentAlive); Assert.Equal(1, HeldLocks);
            if (PreparationFailure == "firewall") throw new IOException("Synthetic platform authorization failed");
        }
        public void Send(UpdateHostMessage message, CancellationToken ct)
        {
            Sent.Add(message);
            if (message.Kind == "confirmed" && LoseConfirmation) throw new EndOfStreamException("Synthetic lost confirmation acknowledgement");
            if (message.Kind == "staged") { transaction = message.TransactionId; StagedObserved = true; OnStaged?.Invoke(); }
            if (message.Kind == "activated")
            {
                Assert.False(ParentAlive); Assert.Equal(0, HeldLocks);
                if (LaunchNewClient) next = Start(Path.Combine(root, "versions", Package.Manifest.Version, "SiloPlayer.exe"));
            }
        }
        public UpdateHostMessage Receive(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            reads++;
            if (reads == 1)
            {
                if (VisibleKind == "disconnected") throw new EndOfStreamException();
                OnVisible?.Invoke();
                ReceivedKinds.Add(VisibleKind); return new(VisibleKind);
            }
            if (reads == 2)
            {
                Assert.True(ParentAlive); Assert.Equal(sourceVersion, SideBySideInstallation.ReadState(root)!.ActiveVersion);
                if (CloseFailure == "disconnected") throw new EndOfStreamException();
                var kind = CloseFailure == "cancel" ? "cancel" : "close"; ReceivedKinds.Add(kind);
                return new(kind, CloseFailure == "wrong-transaction" ? new string('0', 32) : transaction);
            }
            Assert.Equal(0, HeldLocks);
            if (ReadyFailure == "disconnected") throw new EndOfStreamException();
            Assert.NotNull(next); ReceivedKinds.Add("ready");
            return new("ready", ReadyFailure == "wrong-transaction" ? new string('0', 32) : transaction,
                ProcessId: next!.Id, StartUtcTicks: next.StartTime.ToUniversalTime().Ticks + (ReadyFailure == "wrong-start" ? 1 : 0));
        }
        public void WaitForParentExit(CancellationToken ct)
        {
            Assert.True(StagedObserved); Assert.Equal(1, HeldLocks);
            if (WaitReturnsWhileAlive) return;
            // Only this fixture's exact held native dummy process is terminated.
            // The production adapter waits for the user's normal-close exit.
            parent.Kill(); parent.WaitForExit();
        }
        public void VerifyNoOtherClient() { if (OtherClientAlive) throw new IOException("Another synthetic client is running"); }
        public bool IsVersionInUse(string directory)
        {
            if (CleanupInventory == "failure") throw new IOException("Synthetic process inventory is unavailable");
            return CleanupInventory == "unknown" || AdditionalVersionUse?.Invoke(directory) == true || new[] { parent, next, other }.Any(process =>
                process is not null && !process.HasExited &&
                string.Equals(Path.GetDirectoryName(process.MainModule?.FileName), directory, StringComparison.OrdinalIgnoreCase));
        }
        public void RetireUnusedAuthorization(string directory)
        {
            Assert.NotEqual(SideBySideInstallation.ReadState(root)!.ActiveVersion, Path.GetFileName(directory));
            Assert.NotEqual(SideBySideInstallation.ReadState(root)!.PreviousVersion, Path.GetFileName(directory));
            RetiredDirectories.Add(directory);
            if (CleanupInventory == "retirement-failure") throw new IOException("Synthetic firewall retirement failed");
        }
        public bool MatchesReadyProcess(UpdateHostMessage message, string executable)
        {
            Assert.NotNull(next);
            return !next!.HasExited && message.ProcessId == next.Id && message.StartUtcTicks == next.StartTime.ToUniversalTime().Ticks &&
                string.Equals(next.MainModule!.FileName, executable, StringComparison.OrdinalIgnoreCase);
        }
        public void StartOtherClient() => other = Start(Path.Combine(root, "versions", OldVersion, "SiloPlayer.exe"));
        public void StopNewClient() { Assert.NotNull(next); if (!next!.HasExited) { next.Kill(); next.WaitForExit(); } }
        public void ResetForRetry() { reads = 0; transaction = null; CloseFailure = ""; OnStaged = null; ReceivedKinds.Clear(); Sent.Clear(); LaunchNewClient = false; }
        private static Process Start(string executable)
        {
            var process = Process.Start(new ProcessStartInfo(executable)
            { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "-n", "60", "127.0.0.1" }, RedirectStandardOutput = true })!;
            try
            {
                // Match the production readiness boundary: the process has
                // actually loaded its image before it can send ready identity.
                Assert.True(SpinWait.SpinUntil(() =>
                {
                    process.Refresh();
                    return !process.HasExited && process.MainModule is not null;
                }, TimeSpan.FromSeconds(2)), "Synthetic native client did not load its image");
                return process;
            }
            catch { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } process.Dispose(); throw; }
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (var process in new[] { parent, next, other })
                if (process is not null) { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } process.Dispose(); }
        }
        private sealed class Lease(Action dispose) : IDisposable { public void Dispose() => dispose(); }
        private sealed class AuthorityRateLimitHandler(Action count) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                count();
                var response = new HttpResponseMessage(System.Net.HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(45));
                return Task.FromResult(response);
            }
        }
    }
}
