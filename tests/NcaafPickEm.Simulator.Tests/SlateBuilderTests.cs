using NcaafPickEm.Simulator.Catalog;
using NcaafPickEm.Simulator.Engine;

namespace NcaafPickEm.Simulator.Tests;

/// <summary><see cref="SlateBuilder"/>: a quick slate exercises every rule the app has.</summary>
public sealed class SlateBuilderTests
{
    private static readonly DateTimeOffset FirstKickoff = new(2026, 10, 6, 19, 15, 0, TimeSpan.Zero);

    private readonly List<SlateTeam> _teams;
    private readonly Dictionary<Guid, int> _ranks;

    public SlateBuilderTests()
    {
        TeamCatalog catalog = TeamCatalog.Default;
        Dictionary<int, Guid> ids = catalog.Teams.ToDictionary(team => team.Id, _ => Guid.NewGuid());

        _teams = [.. catalog.Teams.Select(team => new SlateTeam(ids[team.Id], team.School, team.Conference, !team.Fcs))];
        _ranks = catalog.PreseasonTop25.Select((cfbdId, index) => (ids[cfbdId], index + 1)).ToDictionary(pair => pair.Item1, pair => pair.Item2);
    }

    [Fact]
    public void GivenTheCatalog_WhenLoaded_ThenEveryFbsTeamHasAKnownConferenceAndThePollHas25()
    {
        TeamCatalog catalog = TeamCatalog.Default;
        HashSet<string> conferences = [.. catalog.Conferences.Select(conference => conference.Abbreviation)];

        catalog.Teams.Where(team => !team.Fcs).Should().HaveCountGreaterThan(130)
            .And.OnlyContain(team => team.Conference != null && conferences.Contains(team.Conference));
        catalog.Teams.Select(team => team.Id).Should().OnlyHaveUniqueItems();
        catalog.PreseasonTop25.Should().HaveCount(25).And.OnlyHaveUniqueItems();
        catalog.PreseasonTop25.Should().OnlyContain(id => catalog.Teams.Any(team => team.Id == id && !team.Fcs));
    }

    [Fact]
    public void GivenDefaultOptions_WhenBuilt_ThenTheSlateCoversEveryRuleTheAppHas()
    {
        IReadOnlyList<SlateGameSpec> slate = SlateBuilder.Build(_teams, _ranks, new SlateOptions(), FirstKickoff, seed: 7);

        slate.Should().HaveCount(14);
        slate.SelectMany(game => new[] { game.HomeTeamId, game.AwayTeamId }).Should().OnlyHaveUniqueItems("a team plays once a week");

        slate.Count(game => !game.IsSaturday).Should().Be(1, "one off-day game for the Saturday rule to leave out");
        slate.Count(game => IsFcs(game.HomeTeamId) || IsFcs(game.AwayTeamId)).Should().Be(1, "one FCS opponent for the FBS rule");
        slate.Count(game => IsRanked(game.HomeTeamId) || IsRanked(game.AwayTeamId)).Should().BeGreaterThanOrEqualTo(8, "the Top 25 rule's games");
        slate.Count(game => game.IsSaturday && !IsRanked(game.HomeTeamId) && !IsRanked(game.AwayTeamId) && !IsFcs(game.AwayTeamId))
            .Should().BeGreaterThanOrEqualTo(2, "unranked games for a commissioner to add by hand");
        slate.Should().Contain(game => game.IsConferenceGame);
        slate.Count(game => game.IsSaturday && game.Spread is decimal line && Math.Abs(line) < SlateBuilder.CloseSpread)
            .Should().BeGreaterThanOrEqualTo(2, "close lines for the close-spread point rule");
    }

    [Fact]
    public void GivenDefaultOptions_WhenBuilt_ThenTheFirstWindowHoldsTheTopRankedGameAtTheFirstKickoff()
    {
        IReadOnlyList<SlateGameSpec> slate = SlateBuilder.Build(_teams, _ranks, new SlateOptions(), FirstKickoff, seed: 7);

        slate.Where(game => game.IsSaturday).Min(game => game.KickoffUtc).Should().Be(FirstKickoff);
        slate.Where(game => game.KickoffUtc == FirstKickoff)
            .Should().Contain(game => _ranks.GetValueOrDefault(game.HomeTeamId) == 1 || _ranks.GetValueOrDefault(game.AwayTeamId) == 1);
        slate.Single(game => !game.IsSaturday).KickoffUtc.Should().BeBefore(FirstKickoff, "the off-day game is the night before");
    }

    [Fact]
    public void GivenTheSameInputs_WhenBuiltTwice_ThenTheSlateIsTheSame()
    {
        SlateBuilder.Build(_teams, _ranks, new SlateOptions(), FirstKickoff, seed: 3)
            .Should().Equal(SlateBuilder.Build(_teams, _ranks, new SlateOptions(), FirstKickoff, seed: 3));
    }

    private bool IsRanked(Guid teamId) => _ranks.ContainsKey(teamId);

    private bool IsFcs(Guid teamId) => !_teams.Single(team => team.TeamId == teamId).IsFbs;
}
