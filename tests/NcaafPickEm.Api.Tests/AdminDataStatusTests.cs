using System.Net;
using System.Net.Http.Json;
using NcaafPickEm.Api.Endpoints;
using NcaafPickEm.Api.Tests.Infrastructure;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Shared.Contracts.Admin;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests;

/// <summary>
/// <c>GET /api/admin/data-status</c>: who may read it, and the shape other phases fill in.
/// </summary>
/// <remarks>
/// The admin group is not league-scoped, so the four-case <see cref="AuthMatrix"/> does not apply:
/// there is no <c>{leagueId}</c> to be a non-member of, and nothing here reveals that a particular
/// league exists. The three cases that do apply — anonymous, a signed-in non-commissioner, and a
/// commissioner — are asserted directly.
/// </remarks>
[Collection(ApiTestCollection.Name)]
public sealed class AdminDataStatusTests
{
    private const string Route = "/api/admin/data-status";

    private readonly ApiTestFixture _fixture;

    public AdminDataStatusTests(ApiTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GivenAnonymousCaller_WhenReadingDataStatus_ThenItIs401()
    {
        using HttpClient client = _fixture.Factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GivenAMemberWhoCommissionsNothing_WhenReadingDataStatus_ThenItIs403()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateClientAs(scenario.MemberUserId);

        using HttpResponseMessage response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GivenAUserWithNoLeagueAtAll_WhenReadingDataStatus_ThenItIs403()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateClientAs(scenario.StrangerUserId);

        using HttpResponseMessage response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GivenACommissioner_WhenReadingDataStatus_ThenItReportsEveryRefreshSliceAndTheProviders()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);
        using HttpClient client = _fixture.Factory.CreateClientAs(scenario.CommissionerUserId);

        using HttpResponseMessage response = await client.GetAsync(Route);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        DataStatusResponse? body = await response.Content.ReadFromJsonAsync<DataStatusResponse>();
        body.Should().NotBeNull();

        // P2-02 writes the DataRefreshStatus rows; until then every slice reports nulls, but all
        // of them are present so the page's shape never depends on what has run.
        body!.Refreshes.Select(refresh => refresh.DataType)
            .Should().BeEquivalentTo(Enum.GetValues<RefreshDataType>());
        body.Refreshes.Should().AllSatisfy(refresh => refresh.LastSuccessUtc.Should().BeNull());

        body.CfbdCallsThisMonth.Should().Be(0);
        body.CfbdWarning.Should().BeFalse();
        body.LiveScoreSource.Should().Be("Fixture");
        body.Unmatched.Should().BeEmpty();
        body.RecentJobs.Should().HaveCountLessThanOrEqualTo(AdminEndpoints.RecentJobCount);
    }

    [Fact]
    public async Task GivenRecordedJobRuns_WhenReadingDataStatus_ThenTheyAreListedNewestFirst()
    {
        LeagueScenario scenario = await TestUsers.CreateLeagueScenarioAsync(_fixture.Factory);

        string jobName = SchedulerHarness.UniqueJobName("seen");
        DateTime now = DateTime.UtcNow;

        await _fixture.Factory.ExecuteDbAsync(async database =>
        {
            database.JobRuns.Add(new JobRun
            {
                Id = Guid.CreateVersion7(),
                JobName = jobName,
                ScheduledForUtc = now.AddMinutes(-5),
                StartedUtc = now.AddMinutes(-5),
                FinishedUtc = now.AddMinutes(-5).AddSeconds(2),
                Success = true,
            });
            await database.SaveChangesAsync();
        });

        using HttpClient client = _fixture.Factory.CreateClientAs(scenario.CommissionerUserId);
        using HttpResponseMessage response = await client.GetAsync(Route);

        DataStatusResponse? body = await response.Content.ReadFromJsonAsync<DataStatusResponse>();
        body.Should().NotBeNull();

        JobRunDto run = body!.RecentJobs.Should().ContainSingle(job => job.JobName == jobName).Subject;
        run.Success.Should().BeTrue();
        run.FinishedUtc.Should().NotBeNull();
        run.ScheduledForUtc.Offset.Should().Be(TimeSpan.Zero);

        body.RecentJobs.Select(job => job.StartedUtc)
            .Should().BeInDescendingOrder();
    }
}
