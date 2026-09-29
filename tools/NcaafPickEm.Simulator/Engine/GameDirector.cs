using Microsoft.Extensions.Options;

namespace NcaafPickEm.Simulator.Engine;

/// <summary>
/// The autoplay loop: every few seconds, <see cref="SimWorld.AutoplayTickAsync"/> moves every game
/// that has kicked off along its plan. Real time, real kickoffs - the only thing compressed is how
/// long a game lasts.
/// </summary>
public sealed class GameDirector : BackgroundService
{
    private readonly SimWorld _world;
    private readonly TimeProvider _timeProvider;
    private readonly SimulatorOptions _options;

    /// <summary>Creates the loop.</summary>
    public GameDirector(SimWorld world, TimeProvider timeProvider, IOptions<SimulatorOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _world = world;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Clamp(_options.AutoplayTickSeconds, 1, 60)), _timeProvider);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await _world.AutoplayTickAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }
}
