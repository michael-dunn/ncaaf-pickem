using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Kiota.Abstractions;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// <see cref="ProviderCallRecorder"/> (P2-03's singleton shape, P2-02's
/// <see cref="ProviderCallRecorder.CountThisMonthAsync"/> addition, D-058): a successful call
/// writes a success row, a failing call writes a failure row with the error and status code (via
/// either <see cref="HttpRequestException"/> or Kiota's <see cref="ApiException"/>), and the
/// monthly count only counts the requested provider.
/// </summary>
/// <remarks>
/// Uses its own throwaway database rather than the shared <see cref="ApiTestFixture"/> one:
/// <c>AdminDataStatusTests</c> asserts <c>CfbdCallsThisMonth == 0</c> against the shared database,
/// which a <c>ProviderCalls</c> row written here for <see cref="ProviderSource.Cfbd"/> would
/// break. The recorder opens its own scope per write, so the test builds a tiny service provider
/// over the throwaway database rather than passing an <see cref="AppDbContext"/> directly.
/// </remarks>
public sealed class ProviderCallRecorderTests : IAsyncLifetime
{
    private SqlTestDatabase _database = null!;

    /// <inheritdoc />
    public async Task InitializeAsync() => _database = await SqlTestDatabase.CreateAsync();

    /// <inheritdoc />
    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task GivenASuccessfulCall_WhenRecorded_ThenOneSuccessRowIsWritten()
    {
        ProviderCallRecorder recorder = CreateRecorder();

        int result = await recorder.RecordAsync(
            ProviderSource.Cfbd,
            "GetTeams",
            _ => Task.FromResult(42));

        result.Should().Be(42);

        await using AppDbContext database = _database.CreateContext();
        ProviderCall call = await database.ProviderCalls.SingleAsync();
        call.Provider.Should().Be("Cfbd");
        call.Operation.Should().Be("GetTeams");
        call.Success.Should().BeTrue();
        call.Error.Should().BeNull();
        call.DurationMs.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GivenAFailingCall_WhenRecorded_ThenOneFailureRowIsWrittenAndTheExceptionPropagates()
    {
        ProviderCallRecorder recorder = CreateRecorder();

        Func<Task> act = () => recorder.RecordAsync<int>(
            ProviderSource.Cfbd,
            "GetGames",
            _ => throw new ApiException("boom") { ResponseStatusCode = 503 });

        await act.Should().ThrowAsync<ApiException>().WithMessage("boom");

        await using AppDbContext database = _database.CreateContext();
        ProviderCall call = await database.ProviderCalls.SingleAsync();
        call.Provider.Should().Be("Cfbd");
        call.Operation.Should().Be("GetGames");
        call.Success.Should().BeFalse();
        call.StatusCode.Should().Be(503);
        call.Error.Should().Contain("boom");
    }

    [Fact]
    public async Task GivenCallsAcrossTwoProviders_WhenCountingThisMonth_ThenOnlyTheRequestedProviderCounts()
    {
        ProviderCallRecorder recorder = CreateRecorder();

        await recorder.RecordAsync(ProviderSource.Cfbd, "GetTeams", _ => Task.FromResult(1));
        await recorder.RecordAsync(ProviderSource.Cfbd, "GetGames", _ => Task.FromResult(1));
        await recorder.RecordAsync(ProviderSource.Espn, "GetScores", _ => Task.FromResult(1));

        (await recorder.CountThisMonthAsync(ProviderSource.Cfbd)).Should().Be(2);
        (await recorder.CountThisMonthAsync(ProviderSource.Espn)).Should().Be(1);
    }

    private ProviderCallRecorder CreateRecorder()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(_database.ConnectionString));
        IServiceScopeFactory scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new ProviderCallRecorder(scopeFactory, TimeProvider.System, NullLogger<ProviderCallRecorder>.Instance);
    }
}
