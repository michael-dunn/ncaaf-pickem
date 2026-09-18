namespace NcaafPickEm.Shared.Contracts.Admin;

/// <summary>
/// One execution of one scheduled job (<c>JobRuns</c>).
/// </summary>
/// <param name="Id">The run.</param>
/// <param name="JobName">
/// The job. A one-shot reads as <c>"{JobName}:{Key}"</c>, for example <c>LockWeek:{gameSetId}</c>.
/// </param>
/// <param name="ScheduledForUtc">The occurrence this run belongs to, not when it started.</param>
/// <param name="StartedUtc">When the run was claimed.</param>
/// <param name="FinishedUtc">When it finished; null while it is still running or if it never returned.</param>
/// <param name="Success">Whether the job completed without throwing.</param>
/// <param name="Error">The failure, truncated to 1000 characters; null on success.</param>
public sealed record JobRunDto(
    Guid Id,
    string JobName,
    DateTimeOffset ScheduledForUtc,
    DateTimeOffset StartedUtc,
    DateTimeOffset? FinishedUtc,
    bool Success,
    string? Error);
