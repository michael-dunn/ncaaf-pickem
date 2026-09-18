using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Infrastructure.Jobs;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// One-shots: the per-week jobs whose due times live in the database rather than in a cron string
/// (P4-02's week lock, P7-03's Saturday reminder).
/// </summary>
/// <remarks>
/// Unlike a cron job there is no "latest occurrence" query to fall back on, so these tests are
/// what proves the unique index on <c>JobRuns(JobName, ScheduledForUtc)</c> is doing the
/// deduplicating.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class OneShotTests
{
    private readonly ApiTestFixture _fixture;

    public OneShotTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenADueOccurrence_WhenTicked_ThenItRunsAndIsRecordedUnderNameAndKey()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset due = now.AddMinutes(-2);
        var job = new RecordingOneShotJob(SchedulerHarness.UniqueJobName("shot"));
        job.Due.Add(new OneShotOccurrence("week7", due));

        await using SchedulerHarness harness = Harness(job);

        await harness.Tick.TickAsync(now, CancellationToken.None);

        job.Ran.Should().ContainSingle().Which.Key.Should().Be("week7");

        JobRun run = (await harness.ReadRunsAsync(job.Name)).Should().ContainSingle().Subject;
        run.JobName.Should().Be($"{job.Name}:week7");
        run.ScheduledForUtc.Should().Be(due.UtcDateTime);
        run.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GivenAnOccurrenceAlreadyRun_WhenTickedAgain_ThenItIsSkipped()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var job = new RecordingOneShotJob(SchedulerHarness.UniqueJobName("shot"));
        job.Due.Add(new OneShotOccurrence("week7", now.AddMinutes(-2)));

        await using SchedulerHarness harness = Harness(job);

        // The source keeps reporting the same occurrence, exactly as a lock job would until the
        // week it just locked stops matching its query.
        await harness.Tick.TickAsync(now, CancellationToken.None);
        await harness.Tick.TickAsync(now.AddMinutes(1), CancellationToken.None);

        job.Polls.Should().Be(2);
        job.Ran.Should().ContainSingle();
        (await harness.ReadRunsAsync(job.Name)).Should().ContainSingle();
    }

    [Fact]
    public async Task GivenAnOccurrenceInTheFuture_WhenTicked_ThenItDoesNotRun()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var job = new RecordingOneShotJob(SchedulerHarness.UniqueJobName("shot"));
        job.Due.Add(new OneShotOccurrence("later", now.AddMinutes(5)));

        await using SchedulerHarness harness = Harness(job);

        await harness.Tick.TickAsync(now, CancellationToken.None);

        job.Ran.Should().BeEmpty();
        (await harness.ReadRunsAsync(job.Name)).Should().BeEmpty();
    }

    /// <remarks>
    /// A moved due time is a new occurrence, which is how a commissioner changing a lock time gets
    /// the week locked at the new moment instead of being deduplicated against the old one.
    /// </remarks>
    [Fact]
    public async Task GivenTheDueTimeMoves_WhenTicked_ThenTheSameKeyRunsAgain()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var job = new RecordingOneShotJob(SchedulerHarness.UniqueJobName("moved"));
        job.Due.Add(new OneShotOccurrence("week7", now.AddMinutes(-10)));

        await using SchedulerHarness harness = Harness(job);

        await harness.Tick.TickAsync(now, CancellationToken.None);

        job.Due.Clear();
        job.Due.Add(new OneShotOccurrence("week7", now.AddMinutes(-5)));

        await harness.Tick.TickAsync(now, CancellationToken.None);

        job.Ran.Should().HaveCount(2);
        (await harness.ReadRunsAsync(job.Name)).Should().HaveCount(2);
    }

    private SchedulerHarness Harness(RecordingOneShotJob job) =>
        SchedulerHarness.Create(_fixture, services => services.AddScoped<IOneShotJob>(_ => job));
}
