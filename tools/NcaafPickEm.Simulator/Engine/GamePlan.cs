namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// How autoplay will play one game out: the final score it is heading for, whether autoplay may
/// touch it at all, and the point it was last re-planned from.
/// </summary>
/// <remarks>
/// The anchor is what keeps scores from ever going backwards: a plan re-made mid-game (a new
/// target, or autoplay switched back on after hand edits) only schedules scoring after
/// <see cref="AnchorProgress"/>, on top of the score the game already had then.
/// </remarks>
/// <param name="TargetHome">Home points at the final whistle.</param>
/// <param name="TargetAway">Away points at the final whistle.</param>
/// <param name="Auto">False once the tester has driven the game by hand; autoplay then leaves it alone.</param>
/// <param name="AnchorProgress">Game progress (0..1) the plan starts from.</param>
/// <param name="AnchorHome">Home points already on the board at the anchor.</param>
/// <param name="AnchorAway">Away points already on the board at the anchor.</param>
public sealed record GamePlan(
    int TargetHome,
    int TargetAway,
    bool Auto = true,
    double AnchorProgress = 0,
    int AnchorHome = 0,
    int AnchorAway = 0)
{
    /// <summary>
    /// The same target, re-planned from <paramref name="progress"/> with the score the game has
    /// now; a target below the current score is raised to it.
    /// </summary>
    /// <param name="progress">Where the game is now (clamped to 0..1).</param>
    /// <param name="home">Home points on the board now.</param>
    /// <param name="away">Away points on the board now.</param>
    public GamePlan AnchoredAt(double progress, int home, int away) => this with
    {
        AnchorProgress = Math.Clamp(progress, 0, 1),
        AnchorHome = home,
        AnchorAway = away,
        TargetHome = Math.Max(TargetHome, home),
        TargetAway = Math.Max(TargetAway, away),
    };
}
