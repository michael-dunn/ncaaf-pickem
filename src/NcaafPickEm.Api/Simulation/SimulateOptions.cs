using System.Globalization;
using NcaafPickEm.Infrastructure.Providers.Fixture;
using NcaafPickEm.Infrastructure.Seeding;

namespace NcaafPickEm.Api.Simulation;

/// <summary>What one <c>simulate</c> invocation was asked to do.</summary>
/// <param name="LeagueName">The league to drive; the demo league by default.</param>
/// <param name="Week">The week to generate and report on; the current week by default.</param>
/// <param name="Snapshot">A fixture score snapshot (1..6) to apply and poll, or null.</param>
/// <param name="Lock">Run <c>LockWeekJob</c> against the week now, whatever the clock says.</param>
/// <param name="TickAt">Run one scheduler pass at this instant, with the clock pinned to it.</param>
public sealed record SimulateOptions(
    string LeagueName,
    int? Week,
    int? Snapshot,
    bool Lock,
    DateTimeOffset? TickAt)
{
    /// <summary>The one-screen help text printed when the arguments do not parse.</summary>
    public const string Usage = """
        Usage: dotnet run --project src/NcaafPickEm.Api -- simulate [options]

          --week <n>          Week to generate and report on (default: the current week).
          --snapshot <1-6>    Apply that fixture score snapshot and run one live-score poll.
          --lock              Run the week lock job now, whatever the clock says.
          --tick <instant>    Run one scheduler pass at that instant, e.g.
                              "2026-10-16T20:00:00-04:00" for the Friday 8 PM ET reminder.
                              The clock is pinned to it for the whole invocation.
          --league <name>     League to drive (default: the fixture demo league).

        Development only, and only with Providers:ReferenceData=Fixture.
        """;

    /// <summary>Reads the options out of the process arguments.</summary>
    /// <param name="args">The arguments, starting with <c>simulate</c>.</param>
    /// <exception cref="FormatException">An argument is unknown, or a value is missing or unreadable.</exception>
    public static SimulateOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string leagueName = FixtureSeeder.DemoLeagueName;
        int? week = null;
        int? snapshot = null;
        bool runLock = false;
        DateTimeOffset? tickAt = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--week":
                    week = ReadInt(args, ref i, "--week");
                    break;

                case "--snapshot":
                    snapshot = ReadInt(args, ref i, "--snapshot");
                    if (snapshot < FixtureSnapshotState.MinSnapshot || snapshot > FixtureSnapshotState.MaxSnapshot)
                    {
                        throw new FormatException(
                            $"--snapshot must be between {FixtureSnapshotState.MinSnapshot} "
                            + $"and {FixtureSnapshotState.MaxSnapshot}.");
                    }

                    break;

                case "--lock":
                    runLock = true;
                    break;

                case "--tick":
                    tickAt = ReadInstant(args, ref i);
                    break;

                case "--league":
                    leagueName = ReadValue(args, ref i, "--league");
                    break;

                default:
                    throw new FormatException($"Unrecognized argument '{args[i]}'.");
            }
        }

        return new SimulateOptions(leagueName, week, snapshot, runLock, tickAt);
    }

    private static string ReadValue(string[] args, ref int index, string name)
    {
        index++;
        if (index >= args.Length)
        {
            throw new FormatException($"{name} needs a value.");
        }

        return args[index];
    }

    private static int ReadInt(string[] args, ref int index, string name)
    {
        string raw = ReadValue(args, ref index, name);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new FormatException($"{name} needs a whole number; got '{raw}'.");
    }

    private static DateTimeOffset ReadInstant(string[] args, ref int index)
    {
        string raw = ReadValue(args, ref index, "--tick");
        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset value)
            ? value
            : throw new FormatException(
                $"--tick needs an instant with an offset, e.g. \"2026-10-16T20:00:00-04:00\"; got '{raw}'.");
    }
}
