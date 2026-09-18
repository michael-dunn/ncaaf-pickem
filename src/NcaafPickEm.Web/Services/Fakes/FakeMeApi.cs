using NcaafPickEm.Shared.Contracts.Auth;
using NcaafPickEm.Shared.Contracts.Leagues;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Web.Services.Fakes;

/// <summary>
/// In-memory <see cref="IMeApi"/> matching <see cref="FakeLeaguesApi"/>'s sample data (Michael,
/// the commissioner of "The Family League"), so the Profile page (P1-03) can be built and
/// screenshotted the same way P1-02's pages were.
/// </summary>
public sealed class FakeMeApi : IMeApi
{
    private static readonly Guid MichaelId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private string _displayName = "Michael";

    /// <inheritdoc />
    public Task<MeResponse> GetMeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildResponse());

    /// <inheritdoc />
    public Task<MeResponse> UpdateDisplayNameAsync(
        UpdateMeRequest request,
        CancellationToken cancellationToken = default)
    {
        string trimmed = request.DisplayName.Trim();
        if (trimmed.Length is 0 or > 30)
        {
            throw new LeaguesApiException(400, "Display name must be 1 to 30 characters.");
        }

        _displayName = trimmed;
        return Task.FromResult(BuildResponse());
    }

    private MeResponse BuildResponse() => new(
        MichaelId,
        "michael@fixture.local",
        _displayName,
        [new LeagueSummary(FakeLeaguesApi.SampleLeagueId, "The Family League", 2026, MembershipRole.Commissioner, 7, SubmissionStatus.Submitted)]);
}
