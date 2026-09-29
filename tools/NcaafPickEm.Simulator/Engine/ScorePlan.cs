using System.Globalization;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// Deterministic game scripts for autoplay (P11-01): a plausible final score from the spread, and
/// the scoreboard at any point of the game on the way there - four quarters with a clock, points
/// in 7s and 3s with the odd 2, 6 or 8.
/// </summary>
/// <remarks>
/// Pure and seeded: the same game, plan and progress always give the same scoreboard, across
/// restarts too (the seed comes from the game id through a stable hash, never
/// <see cref="HashCode"/>, which is randomized per process). Scoring events after the plan's anchor
/// only ever add points, so a game's score never goes down as progress rises.
/// </remarks>
public static class ScorePlan
{
    /// <summary>Scoring stops this close to the end, so the last minute is not a flurry.</summary>
    private const double LastScoringProgress = 0.97;

    private const int SecondsPerQuarter = 15 * 60;

    /// <summary>A stable seed for a game, the same in every process.</summary>
    /// <param name="gameId">The game's id.</param>
    public static int SeedFor(Guid gameId)
    {
        Span<byte> bytes = stackalloc byte[16];
        gameId.TryWriteBytes(bytes);

        uint hash = 2166136261;
        foreach (byte value in bytes)
        {
            hash = (hash ^ value) * 16777619;
        }

        return unchecked((int)hash);
    }

    /// <summary>
    /// A final score for a game with this seed and spread (home-relative, negative = home
    /// favoured): the favourite usually wins by about the spread, roughly 48 points in all, and
    /// never a tie - a tie is only ever the tester's choice.
    /// </summary>
    /// <param name="seed">The game's seed.</param>
    /// <param name="spread">The home spread, or null for an even game.</param>
    public static (int Home, int Away) DefaultTarget(int seed, decimal? spread)
    {
        var random = new Random(Mix(seed, 17));

        double expectedHomeMargin = spread is decimal line ? (double)-line : 0;
        double margin = expectedHomeMargin + (Normal(random) * 13);
        double total = Math.Clamp(48 + (Normal(random) * 13), 13, 90);

        int home = Playable((int)Math.Round((total + margin) / 2, MidpointRounding.AwayFromZero));
        int away = Playable((int)Math.Round((total - margin) / 2, MidpointRounding.AwayFromZero));

        if (home == away)
        {
            if (random.NextDouble() < 0.5)
            {
                home += 3;
            }
            else
            {
                away += 3;
            }
        }

        return (home, away);
    }

    /// <summary>The scoreboard at <paramref name="progress"/> (0 = kickoff, 1 = final whistle).</summary>
    /// <param name="seed">The game's seed.</param>
    /// <param name="plan">Where the game is heading and where it was re-planned from.</param>
    /// <param name="progress">Elapsed game time over the game's length; negative before kickoff.</param>
    public static PlannedState At(int seed, GamePlan plan, double progress)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (progress < 0)
        {
            return new PlannedState(GameStatus.Scheduled, null, null, null, null);
        }

        if (progress >= 1)
        {
            return new PlannedState(GameStatus.Final, plan.TargetHome, plan.TargetAway, null, null);
        }

        int home = plan.AnchorHome;
        int away = plan.AnchorAway;
        foreach (ScoringEvent scoring in Events(seed, plan))
        {
            if (scoring.Progress > progress)
            {
                break;
            }

            if (scoring.Home)
            {
                home += scoring.Points;
            }
            else
            {
                away += scoring.Points;
            }
        }

        (byte period, string clock) = Clock(progress);
        return new PlannedState(GameStatus.InProgress, home, away, period, clock);
    }

    /// <summary>The quarter and game clock at <paramref name="progress"/>, e.g. (3, "7:30").</summary>
    /// <param name="progress">Elapsed game time over the game's length, 0..1.</param>
    public static (byte Period, string Clock) Clock(double progress)
    {
        double clamped = Math.Clamp(progress, 0, 0.999999);
        int quarter = (int)(clamped * 4) + 1;
        double intoQuarter = (clamped * 4) - (quarter - 1);
        int secondsLeft = (int)Math.Round((1 - intoQuarter) * SecondsPerQuarter, MidpointRounding.AwayFromZero);

        return ((byte)quarter, string.Create(
            CultureInfo.InvariantCulture,
            $"{secondsLeft / 60}:{secondsLeft % 60:00}"));
    }

    /// <summary>Every scoring play the plan makes after its anchor, in game order.</summary>
    internal static IReadOnlyList<ScoringEvent> Events(int seed, GamePlan plan)
    {
        var random = new Random(Mix(
            seed,
            plan.TargetHome,
            plan.TargetAway,
            plan.AnchorHome,
            plan.AnchorAway,
            (int)Math.Round(plan.AnchorProgress * 1_000_000)));

        double from = Math.Min(plan.AnchorProgress, LastScoringProgress);
        var events = new List<ScoringEvent>();

        foreach (int points in Chunks(plan.TargetHome - plan.AnchorHome, random))
        {
            events.Add(new ScoringEvent(from + (random.NextDouble() * (LastScoringProgress - from)), true, points));
        }

        foreach (int points in Chunks(plan.TargetAway - plan.AnchorAway, random))
        {
            events.Add(new ScoringEvent(from + (random.NextDouble() * (LastScoringProgress - from)), false, points));
        }

        events.Sort((left, right) => left.Progress.CompareTo(right.Progress));
        return events;
    }

    /// <summary>Splits a team's remaining points into football-shaped scores.</summary>
    private static List<int> Chunks(int points, Random random)
    {
        var chunks = new List<int>();

        while (points > 0)
        {
            int chunk = points switch
            {
                >= 10 => random.NextDouble() < 0.72 ? 7 : 3,
                9 => random.NextDouble() < 0.5 ? 7 : 3,
                8 => 8,
                7 => 7,
                6 => random.NextDouble() < 0.5 ? 6 : 3,
                5 => 3,
                4 or 2 => 2,
                3 => 3,
                _ => 1,
            };

            chunks.Add(chunk);
            points -= chunk;
        }

        return chunks;
    }

    /// <summary>A final score a team plausibly finishes on: never 1, and not a lone safety.</summary>
    private static int Playable(int points) => points switch
    {
        < 0 => 0,
        1 => 0,
        2 => 3,
        _ => points,
    };

    private static double Normal(Random random)
    {
        // Box-Muller; 1 - NextDouble() keeps the logarithm's argument off zero.
        double u1 = 1 - random.NextDouble();
        double u2 = random.NextDouble();
        return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
    }

    private static int Mix(params ReadOnlySpan<int> values)
    {
        uint hash = 2166136261;
        foreach (int value in values)
        {
            hash = (hash ^ unchecked((uint)value)) * 16777619;
        }

        return unchecked((int)hash);
    }

    /// <summary>One scoring play: when in the game, for which side, how many points.</summary>
    internal readonly record struct ScoringEvent(double Progress, bool Home, int Points);
}
