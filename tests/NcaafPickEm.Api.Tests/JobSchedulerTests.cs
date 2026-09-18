using Microsoft.Extensions.DependencyInjection;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Infrastructure.Jobs;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// The P0-06 card's "Done when" list for cron jobs: a job scheduled for a past minute runs once,
/// two ticks for the same minute do not double-insert, and the disabled flag runs nothing.
/// </summary>
[Collection(ApiTestCollection.Name)]
public sealed class JobSchedulerTests
{
    private readonly ApiTestFixture _fixture;

    public JobSchedulerTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenJobDueTenMinutesAgo_WhenTicked_ThenItRunsOnceAndIsRecorded()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset target = now.AddMinutes(-10);
        var job = new RecordingScheduledJob(
            SchedulerHarness.UniqueJobName("cron"), SchedulerHarness.CronForMinuteOf(target));

        await using SchedulerHarness harness = Harness(job);

        await harness.Tick.TickAsync(now, CancellationToken.None);

        job.Occurrences.Should().ContainSingle()
            .Which.Should().Be(TruncateToMinute(target));

        JobRun run = (await harness.ReadRunsAsync(job.Name)).Should().ContainSingle().Subject;
        run.JobName.Should().Be(job.Name);
        run.ScheduledForUtc.Should().Be(TruncateToMinute(target).UtcDateTime);
        run.Success.Should().BeTrue();
        run.FinishedUtc.Should().NotBeNull();
        run.Error.Should().BeNull();
    }

    [Fact]
    public async Task GivenAJobAlreadyRun_WhenTickedAgainForTheSameMinute_ThenNothingIsInserted()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset target = now.AddMinutes(-3);
        var job = new RecordingScheduledJob(
            SchedulerHarness.UniqueJobName("cron"), SchedulerHarness.CronForMinuteOf(target));

        await using SchedulerHarness harness = Harness(job);

        await harness.Tick.TickAsync(now, CancellationToken.None);
        await harness.Tick.TickAsync(now, CancellationToken.None);

        job.Occurrences.Should().ContainSingle();
        (await harness.ReadRunsAsync(job.Name)).Should().ContainSingle();
    }

    /// <remarks>
    /// Two ticks racing for the same occurrence is what a restart or a second instance looks like.
    /// Whichever guard wins — the "latest recorded occurrence" query or the unique index on
    /// <c>JobRuns(JobName, ScheduledForUtc)</c> — the job must run exactly once.
    /// </remarks>
    [Fact]
    public async Task GivenTwoTicksAtOnce_WhenBothSeeTheSameOccurrence_ThenTheJobRunsOnce()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset target = now.AddMinutes(-4);
        string name = SchedulerHarness.UniqueJobName("race");
        string cron = SchedulerHarness.CronForMinuteOf(target);

        var first = new RecordingScheduledJob(name, cron);
        var second = new RecordingScheduledJob(name, cron);

        await using SchedulerHarness one = Harness(first);
        await using SchedulerHarness two = Harness(second);

        await Task.WhenAll(
            one.Tick.TickAsync(now, CancellationToken.None),
            two.Tick.TickAsync(now, CancellationToken.None));

        (first.Occurrences.Count + second.Occurrences.Count).Should().Be(1);
        (await one.ReadRunsAsync(name)).Should().ContainSingle();
    }

    [Fact]
    public async Task GivenJobsDisabled_WhenTheSchedulerStarts_ThenNothingRuns()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var job = new RecordingScheduledJob(
            SchedulerHarness.UniqueJobName("off"), SchedulerHarness.CronForMinuteOf(now.AddMinutes(-2)));

        await using SchedulerHarness harness = Harness(job, enabled: false);
        JobScheduler scheduler = harness.CreateHostedScheduler();

        await scheduler.StartAsync(CancellationToken.None);
        await scheduler.StopAsync(CancellationToken.None);

        job.Occurrences.Should().BeEmpty();
        (await harness.ReadRunsAsync(job.Name)).Should().BeEmpty();
    }

    /// <remarks>
    /// The only test that exercises the <see cref="JobScheduler"/> loop itself rather than
    /// <see cref="SchedulerTick"/>: it proves the first tick happens at startup, which is what the
    /// heartbeat relies on.
    /// </remarks>
    [Fact]
    public async Task GivenJobsEnabled_WhenTheSchedulerStarts_ThenItTicksImmediately()
    {
        DateTimeOffset target = DateTimeOffset.UtcNow.AddMinutes(-2);
        var job = new RecordingScheduledJob(
            SchedulerHarness.UniqueJobName("boot"), SchedulerHarness.CronForMinuteOf(target));

        await using SchedulerHarness harness = Harness(job);
        JobScheduler scheduler = harness.CreateHostedScheduler();

        await scheduler.StartAsync(CancellationToken.None);
        await job.Ran.Task.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        await scheduler.StopAsync(CancellationToken.None);

        job.Occurrences.Should().ContainSingle();
        (await harness.ReadRunsAsync(job.Name)).Should().ContainSingle()
            .Which.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GivenAJobThatThrows_WhenTicked_ThenTheRunIsRecordedAsFailed()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var job = new RecordingScheduledJob(
            SchedulerHarness.UniqueJobName("bad"), SchedulerHarness.CronForMinuteOf(now.AddMinutes(-1)))
        {
            ThrowOnRun = new InvalidOperationException("job blew up"),
        };

        await using SchedulerHarness harness = Harness(job);

        await harness.Tick.TickAsync(now, CancellationToken.None);

        JobRun run = (await harness.ReadRunsAsync(job.Name)).Should().ContainSingle().Subject;
        run.Success.Should().BeFalse();
        run.FinishedUtc.Should().NotBeNull();
        run.Error.Should().Contain("job blew up");
    }

    /// <remarks>
    /// The catch-up window is what stops a machine that was off overnight replaying a whole day of
    /// schedule when it comes back.
    /// </remarks>
    [Fact]
    public async Task GivenAnOccurrenceOlderThanTheCatchUpWindow_WhenTicked_ThenItIsNotRun()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var job = new RecordingScheduledJob(
            SchedulerHarness.UniqueJobName("old"), SchedulerHarness.CronForMinuteOf(now.AddMinutes(-30)));

        await using SchedulerHarness harness = Harness(job, catchUpMinutes: 10);

        await harness.Tick.TickAsync(now, CancellationToken.None);

        job.Occurrences.Should().BeEmpty();
        (await harness.ReadRunsAsync(job.Name)).Should().BeEmpty();
    }

    private static DateTimeOffset TruncateToMinute(DateTimeOffset value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, value.Offset);

    private SchedulerHarness Harness(RecordingScheduledJob job, bool enabled = true, int catchUpMinutes = 60) =>
        SchedulerHarness.Create(
            _fixture,
            services => services.AddScoped<IScheduledJob>(_ => job),
            enabled,
            catchUpMinutes);
}
