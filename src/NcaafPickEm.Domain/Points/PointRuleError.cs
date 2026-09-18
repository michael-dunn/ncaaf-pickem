namespace NcaafPickEm.Domain.Points;

/// <summary>
/// One thing wrong with a league's point configuration, named the way a validation problem
/// response wants it (Feature 03).
/// </summary>
/// <param name="Field">Which field is wrong: <c>DefaultPointValue</c>, or
/// <c>Rules[i].&lt;Field&gt;</c> for the rule at index <c>i</c> of the list as it was passed in.</param>
/// <param name="Message">What is wrong with it, in words a commissioner can read.</param>
public sealed record PointRuleError(string Field, string Message);
