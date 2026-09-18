using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NcaafPickEm.Infrastructure.Providers.Models;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Infrastructure.Providers.Espn;

/// <summary>
/// Turns one ESPN scoreboard response into <see cref="LiveScoreUpdate"/> records. Pure and
/// static so it can be tested straight against the captured payloads in
/// <c>tests/NcaafPickEm.Fixtures/Real</c> with no HTTP.
/// </summary>
/// <remarks>
/// Field choices follow <c>Implementation/spikes/providers.md</c>: match on <c>team.location</c>
/// (the school name with no mascot) rather than <c>displayName</c>; <c>events[].id</c> and
/// <c>team.id</c> arrive as strings; <c>events[].date</c> has minute precision and no seconds, so
/// it is parsed with <see cref="DateTimeOffset.Parse(string, IFormatProvider, DateTimeStyles)"/>
/// and never <c>ParseExact</c>; scores are dropped while the game is scheduled because ESPN
/// reports <c>"0"</c> for both sides before kickoff; <c>odds[0].spread</c> is already
/// home-relative and needs no sign conversion.
/// </remarks>
public static class EspnScoreboardParser
{
    private static readonly ConcurrentDictionary<string, byte> LoggedUnknownStatuses = new(StringComparer.Ordinal);

    /// <summary>Parses a scoreboard response body.</summary>
    /// <param name="json">The raw response body.</param>
    /// <param name="logger">Optional logger; unknown status names are logged once each.</param>
    /// <exception cref="JsonException">The body is not JSON.</exception>
    public static IReadOnlyList<LiveScoreUpdate> Parse(string json, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using JsonDocument document = JsonDocument.Parse(json);
        return Parse(document.RootElement, logger);
    }

    /// <summary>Parses an already-materialized scoreboard response.</summary>
    /// <param name="root">The response root object.</param>
    /// <param name="logger">Optional logger; unknown status names are logged once each.</param>
    public static IReadOnlyList<LiveScoreUpdate> Parse(JsonElement root, ILogger? logger = null)
    {
        if (!root.TryGetProperty("events", out JsonElement events) || events.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<LiveScoreUpdate> updates = [];
        foreach (JsonElement sportEvent in events.EnumerateArray())
        {
            LiveScoreUpdate? update = ParseEvent(sportEvent, logger);
            if (update is not null)
            {
                updates.Add(update);
            }
        }

        return updates;
    }

    private static LiveScoreUpdate? ParseEvent(JsonElement sportEvent, ILogger? logger)
    {
        string? eventId = GetString(sportEvent, "id");
        if (string.IsNullOrWhiteSpace(eventId))
        {
            return null;
        }

        JsonElement competition = FirstOrDefault(sportEvent, "competitions");
        JsonElement status = competition.ValueKind == JsonValueKind.Object && competition.TryGetProperty("status", out JsonElement competitionStatus)
            ? competitionStatus
            : GetObject(sportEvent, "status");

        JsonElement statusType = GetObject(status, "type");
        string? statusName = GetString(statusType, "name");
        string? state = GetString(statusType, "state");
        bool completed = GetBoolean(statusType, "completed");

        GameStatus? mapped = EspnStatusMapper.Map(statusName, state, completed, out bool recognized);
        if (!recognized && logger is not null && !string.IsNullOrWhiteSpace(statusName)
            && LoggedUnknownStatuses.TryAdd(statusName, 0))
        {
            logger.LogWarning(
                "Unknown ESPN status name {StatusName}; fell back to state {State} -> {MappedStatus}",
                statusName,
                state,
                mapped);
        }

        if (mapped is not GameStatus gameStatus)
        {
            logger?.LogWarning(
                "ESPN event {EventId} has neither a known status name ({StatusName}) nor a usable state ({State}); skipped",
                eventId,
                statusName,
                state);
            return null;
        }

        JsonElement competitors = competition.ValueKind == JsonValueKind.Object
            && competition.TryGetProperty("competitors", out JsonElement found)
            && found.ValueKind == JsonValueKind.Array
                ? found
                : default;

        if (competitors.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        Side? home = null;
        Side? away = null;
        foreach (JsonElement competitor in competitors.EnumerateArray())
        {
            string? homeAway = GetString(competitor, "homeAway");
            Side side = ReadSide(competitor, gameStatus);
            if (string.Equals(homeAway, "home", StringComparison.OrdinalIgnoreCase))
            {
                home = side;
            }
            else if (string.Equals(homeAway, "away", StringComparison.OrdinalIgnoreCase))
            {
                away = side;
            }
        }

        if (home is null || away is null)
        {
            logger?.LogWarning("ESPN event {EventId} has no home/away pair; skipped", eventId);
            return null;
        }

        DateTime kickoffUtc = ParseKickoff(sportEvent, competition);

        return new LiveScoreUpdate(
            eventId,
            kickoffUtc,
            home.Name,
            home.Abbreviation,
            home.TeamId,
            home.Score,
            away.Name,
            away.Abbreviation,
            away.TeamId,
            away.Score,
            gameStatus,
            statusName ?? state ?? string.Empty,
            completed,
            ReadPeriod(status, gameStatus),
            ReadClock(status, gameStatus),
            ReadSpread(competition),
            ProviderSource.Espn);
    }

    private static Side ReadSide(JsonElement competitor, GameStatus status)
    {
        JsonElement team = GetObject(competitor, "team");

        // team.location is the school name without the mascot and lines up with CFBD's `school`;
        // displayName is only a fallback for a payload that somehow omits it.
        string name = GetString(team, "location")
            ?? GetString(team, "displayName")
            ?? string.Empty;

        // A scheduled game reports "0" for both sides. Reading that as a real 0-0 would fabricate
        // a tie the moment the status mapping ever said Final.
        int? score = status == GameStatus.Scheduled ? null : ParseScore(GetString(competitor, "score"));

        return new Side(name, GetString(team, "abbreviation") ?? string.Empty, GetString(team, "id"), score);
    }

    private static DateTime ParseKickoff(JsonElement sportEvent, JsonElement competition)
    {
        string? date = GetString(sportEvent, "date") ?? GetString(competition, "date");
        if (date is null)
        {
            return default;
        }

        // "2026-09-12T23:30Z" - minute precision, no seconds. ParseExact would throw.
        return DateTimeOffset
            .Parse(date, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .UtcDateTime;
    }

    private static byte? ReadPeriod(JsonElement status, GameStatus gameStatus)
    {
        if (gameStatus != GameStatus.InProgress
            || !status.TryGetProperty("period", out JsonElement period)
            || period.ValueKind != JsonValueKind.Number
            || !period.TryGetInt32(out int value)
            || value <= 0)
        {
            return null;
        }

        return (byte)Math.Min(value, byte.MaxValue);
    }

    private static string? ReadClock(JsonElement status, GameStatus gameStatus)
    {
        if (gameStatus != GameStatus.InProgress)
        {
            return null;
        }

        string? clock = GetString(status, "displayClock");
        return string.IsNullOrWhiteSpace(clock) ? null : clock;
    }

    private static decimal? ReadSpread(JsonElement competition)
    {
        JsonElement odds = FirstOrDefault(competition, "odds");
        if (odds.ValueKind != JsonValueKind.Object
            || !odds.TryGetProperty("spread", out JsonElement spread)
            || spread.ValueKind != JsonValueKind.Number
            || !spread.TryGetDecimal(out decimal value))
        {
            return null;
        }

        return value;
    }

    private static int? ParseScore(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int score) ? score : null;

    private static JsonElement FirstOrDefault(JsonElement parent, string arrayName)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(arrayName, out JsonElement array)
            || array.ValueKind != JsonValueKind.Array)
        {
            return default;
        }

        foreach (JsonElement item in array.EnumerateArray())
        {
            return item;
        }

        return default;
    }

    private static JsonElement GetObject(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.Object
            ? value
            : default;

    private static string? GetString(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetBoolean(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out JsonElement value)
        && value.ValueKind == JsonValueKind.True;

    private sealed record Side(string Name, string Abbreviation, string? TeamId, int? Score);
}
