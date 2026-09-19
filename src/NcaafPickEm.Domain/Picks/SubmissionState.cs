using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Domain.Picks;

/// <summary>
/// What <see cref="SubmissionStatusCalculator"/> works out about one member's week.
/// </summary>
/// <param name="Status">
/// <see cref="SubmissionStatus.NotStarted"/>, <see cref="SubmissionStatus.InProgress"/>, or
/// <see cref="SubmissionStatus.Submitted"/>. The calculator never produces
/// <see cref="SubmissionStatus.Locked"/> or <see cref="SubmissionStatus.Incomplete"/>: those are
/// the lock job's to write, and are preserved once written.
/// </param>
/// <param name="PickedCount">Picks the member holds on active games.</param>
/// <param name="TotalCount">Active games in the week's set.</param>
public readonly record struct SubmissionState(SubmissionStatus Status, int PickedCount, int TotalCount);
