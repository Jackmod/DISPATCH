using Dispatch.Core.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Dispatch.Core.Tests.Resilience;

/// <summary>The retry policy: only transient failures retry, and only up to the limit.</summary>
public sealed class RetryPolicyTests
{
    // A policy that never actually waits, so the backoff schedule is exercised
    // without the test taking seventeen seconds.
    private static RetryPolicy Instant() =>
        new(NullLogger<RetryPolicy>.Instance, delay: (_, _) => Task.CompletedTask, jitter: () => 0.5);

    [Fact]
    public async Task A_successful_operation_runs_once()
    {
        var calls = 0;
        var policy = Instant();

        var result = await policy.ExecuteAsync(_ => { calls++; return Task.FromResult(42); }, "test");

        result.Should().Be(42);
        calls.Should().Be(1);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_up_to_the_limit()
    {
        var calls = 0;
        var policy = Instant();

        var act = async () => await policy.ExecuteAsync<int>(_ =>
        {
            calls++;
            throw new RetryableException("flaky", FailureClass.Transient);
        }, "test");

        await act.Should().ThrowAsync<RetryableException>();
        calls.Should().Be(policy.MaxAttempts, "every attempt is used before giving up");
    }

    [Fact]
    public async Task A_transient_failure_that_then_succeeds_returns_the_result()
    {
        var calls = 0;
        var policy = Instant();

        var result = await policy.ExecuteAsync(_ =>
        {
            calls++;
            if (calls < 2)
            {
                throw new RetryableException("first time", FailureClass.Transient);
            }

            return Task.FromResult("ok");
        }, "test");

        result.Should().Be("ok");
        calls.Should().Be(2);
    }

    [Fact]
    public async Task A_permanent_failure_is_not_retried()
    {
        // A corrupt archive or a permission error will not fix itself; retrying
        // just wastes the user's time.
        var calls = 0;
        var policy = Instant();

        var act = async () => await policy.ExecuteAsync<int>(_ =>
        {
            calls++;
            throw new RetryableException("corrupt", FailureClass.Permanent);
        }, "test");

        await act.Should().ThrowAsync<RetryableException>();
        calls.Should().Be(1, "permanent failures are tried exactly once");
    }

    [Fact]
    public async Task An_unclassified_exception_is_treated_as_permanent()
    {
        var calls = 0;
        var policy = Instant();

        var act = async () => await policy.ExecuteAsync<int>(_ =>
        {
            calls++;
            throw new InvalidOperationException("unexpected");
        }, "test");

        await act.Should().ThrowAsync<InvalidOperationException>();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_stops_retrying()
    {
        var policy = Instant();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = async () => await policy.ExecuteAsync<int>(
            _ => throw new RetryableException("x", FailureClass.Transient), "test", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

/// <summary>The error catalogue: every failure has a human explanation and a way out.</summary>
public sealed class ErrorCatalogueTests
{
    [Fact]
    public void A_known_code_resolves_to_its_spec()
    {
        var spec = ErrorCatalogue.Resolve("AV_QUARANTINE");

        spec.Code.Should().Be("AV_QUARANTINE");
        spec.Title.Should().Contain("antivirus");
        spec.Actions.Should().NotBeEmpty();
    }

    [Fact]
    public void An_unknown_code_resolves_to_a_generic_card_with_the_run_id()
    {
        var spec = ErrorCatalogue.Resolve("NEVER_HEARD_OF_IT", runId: "run-7f3a");

        spec.Code.Should().Be("UNKNOWN");
        spec.Body.Should().Contain("run-7f3a");
        spec.Actions.Should().Contain(a => a.Command == "copy-diagnostics");
    }

    [Fact]
    public void No_spec_leaks_a_stack_trace_or_jargon()
    {
        // The whole point of the catalogue is that no exception text reaches the
        // user; every body is prose.
        foreach (var code in ErrorCatalogue.Codes)
        {
            var spec = ErrorCatalogue.Resolve(code);
            spec.Title.Should().NotContainAny("Exception", "null", "0x");
            spec.Body.Should().NotContainAny("Exception", "\tat ", "System.");
            spec.Actions.Should().NotBeEmpty("every error offers a way forward");
        }
    }

    [Fact]
    public void The_build_mismatch_spec_names_the_real_cause()
    {
        // This is behind most "LSPDFR is broken" reports; the wording has to say
        // so plainly.
        var spec = ErrorCatalogue.Resolve("BUILD_MISMATCH");

        spec.Body.Should().Contain("locked to the exact build");
    }
}

/// <summary>Staging: path-traversal defence and fresh directories.</summary>
public sealed class StagingAreaTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "dispatch-staging", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void A_normal_member_resolves_inside_the_base()
    {
        var resolved = StagingArea.ResolveWithin(_root, "plugins/StopThePed.dll");

        resolved.Should().StartWith(Path.GetFullPath(_root));
    }

    [Theory]
    [InlineData("../../Windows/System32/evil.dll")]
    [InlineData("..\\..\\escape.txt")]
    [InlineData("/etc/passwd")]
    [InlineData("subdir/../../../outside.dll")]
    public void A_member_escaping_the_base_is_rejected(string member)
    {
        // The one archive attack that can write anywhere on the disk. Non-
        // negotiable that it is refused.
        var act = () => StagingArea.ResolveWithin(_root, member);

        act.Should().Throw<UnsafeArchivePathException>();
    }

    [Fact]
    public void A_mod_directory_is_always_fresh()
    {
        // A partial extraction from a crashed run is never trusted.
        var area = new StagingArea(_root, "run-1");

        var first = area.PrepareModDirectory("stoptheped");
        File.WriteAllText(Path.Combine(first, "leftover.dll"), "x");

        var second = area.PrepareModDirectory("stoptheped");

        Directory.GetFiles(second).Should().BeEmpty("the stale extraction was cleared");
    }

    [Fact]
    public void Purging_removes_the_run_tree()
    {
        var area = new StagingArea(_root, "run-1");
        area.PrepareModDirectory("mod");

        area.Purge();

        Directory.Exists(area.Root).Should().BeFalse();
    }
}

/// <summary>Preflight: refuse before starting a run that cannot finish.</summary>
public sealed class PreflightCheckTests
{
    private sealed class FakeEnv : IEnvironmentProbe
    {
        public IReadOnlyList<string> Processes { get; set; } = [];
        public bool Writable { get; set; } = true;
        public long? Free { get; set; } = 100L * 1024 * 1024 * 1024;
        public bool Network { get; set; } = true;
        public bool OtherInstance { get; set; }
        public bool SyncFolder { get; set; }

        public IReadOnlyList<string> RunningGameProcesses() => Processes;
        public bool CanWriteTo(string path) => Writable;
        public long? FreeBytesOn(string path) => Free;
        public bool IsNetworkReachable() => Network;
        public bool AnotherInstanceRunning() => OtherInstance;
        public bool IsInsideSyncFolder(string path) => SyncFolder;
    }

    private const long OneGb = 1024L * 1024 * 1024;

    [Fact]
    public void A_clean_environment_can_proceed()
    {
        var result = new PreflightCheck(new FakeEnv()).Run(@"C:\Games\GTAV", OneGb);

        result.CanProceed.Should().BeTrue();
        result.Blockers.Should().BeEmpty();
    }

    [Fact]
    public void A_running_game_blocks_the_run()
    {
        var env = new FakeEnv { Processes = ["GTA5", "RagePluginHook"] };

        var result = new PreflightCheck(env).Run(@"C:\Games\GTAV", OneGb);

        result.CanProceed.Should().BeFalse();
        result.Blockers.Should().Contain(f => f.Code == "GAME_RUNNING");
        result.Blockers[0].Summary.Should().Contain("GTA5");
    }

    [Fact]
    public void Too_little_disk_space_blocks_the_run()
    {
        // Required plus 50% headroom must fit.
        var env = new FakeEnv { Free = OneGb };

        var result = new PreflightCheck(env).Run(@"C:\Games\GTAV", OneGb);

        result.CanProceed.Should().BeFalse();
        result.Blockers.Should().Contain(f => f.Code == "DISK_FULL");
    }

    [Fact]
    public void Disk_space_requires_fifty_percent_headroom()
    {
        // Exactly the required size is not enough; staging needs half again.
        var env = new FakeEnv { Free = (long)(OneGb * 1.2) };

        var result = new PreflightCheck(env).Run(@"C:\Games\GTAV", OneGb);

        result.Blockers.Should().Contain(f => f.Code == "DISK_FULL");
    }

    [Fact]
    public void No_network_blocks_the_run()
    {
        var result = new PreflightCheck(new FakeEnv { Network = false }).Run(@"C:\Games\GTAV", OneGb);

        result.Blockers.Should().Contain(f => f.Code == "NO_NETWORK");
    }

    [Fact]
    public void A_folder_needing_elevation_blocks_the_run()
    {
        var result = new PreflightCheck(new FakeEnv { Writable = false }).Run(@"C:\Program Files\GTAV", OneGb);

        result.Blockers.Should().Contain(f => f.Code == "NEEDS_ELEVATION");
    }

    [Fact]
    public void A_sync_folder_warns_but_does_not_block()
    {
        // A OneDrive game folder is a real hazard, but not a reason to refuse
        // outright — the user may know what they are doing.
        var result = new PreflightCheck(new FakeEnv { SyncFolder = true }).Run(@"C:\Users\me\OneDrive\GTAV", OneGb);

        result.CanProceed.Should().BeTrue("a warning does not block");
        result.Warnings.Should().Contain(f => f.Code == "SYNC_FOLDER");
    }

    [Fact]
    public void Every_blocking_failure_carries_a_remedy_code()
    {
        // A blocker with no code is a dead end for the user.
        var env = new FakeEnv { Processes = ["GTA5"], Network = false, Writable = false, Free = 0 };

        var result = new PreflightCheck(env).Run(@"C:\Program Files\GTAV", OneGb);

        result.Blockers.Should().OnlyContain(f => f.Code != null);
        foreach (var blocker in result.Blockers)
        {
            ErrorCatalogue.Resolve(blocker.Code!).Should().NotBeNull();
        }
    }
}
