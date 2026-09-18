using Microsoft.EntityFrameworkCore;
using Microsoft.Kiota.Abstractions;
using NcaafPickEm.Domain.Operations;
using NcaafPickEm.Infrastructure.Data;
using NcaafPickEm.Infrastructure.Providers;
using NcaafPickEm.Shared.Enums;

namespace NcaafPickEm.Api.Tests.Infrastructure;

/// <summary>
/// <see cref="ProviderCallRecorder"/> (P2-02): a successful call writes a success row, a failing
/// call writes a failure row with the error and status code, and the monthly count only counts
/// the requested provider.
/// </summary>
/// <remarks>
/// Uses its own throwaway database rather than the shared <see cref="ApiTestFixture"/> one:
/// <c>AdminDataStatusTests</c> asserts <c>CfbdCallsThisMonth == 0</c> against the shared database,
/// which a <c>ProviderCalls</c> row written here for <see cref="ProviderSource.Cfbd"/> would
/// break.
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
        await using AppDbContext database = _database.CreateContext();
        var recorder = new ProviderCallRecorder(database, TimeProvider.System);

        int result = await recorder.RecordAsync(
            ProviderSource.Cfbd,
            "GetTeams",
            _ => Task.FromResult(42));

        result.Should().Be(42);

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
        await using AppDbContext database = _database.CreateContext();
        var recorder = new ProviderCallRecorder(database, TimeProvider.System);

        Func<Task> act = () => recorder.RecordAsync<int>(
            ProviderSource.Cfbd,
            "GetGames",
            _ => throw new ApiException("boom") { ResponseStatusCode = 503 });

        await act.Should().ThrowAsync<ApiException>().WithMessage("boom");

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
        await using AppDbContext database = _database.CreateContext();
        var recorder = new ProviderCallRecorder(database, TimeProvider.System);

        await recorder.RecordAsync(ProviderSource.Cfbd, "GetTeams", _ => Task.FromResult(1));
        await recorder.RecordAsync(ProviderSource.Cfbd, "GetGames", _ => Task.FromResult(1));
        await recorder.RecordAsync(ProviderSource.Espn, "GetScores", _ => Task.FromResult(1));

        (await recorder.CountThisMonthAsync(ProviderSource.Cfbd)).Should().Be(2);
        (await recorder.CountThisMonthAsync(ProviderSource.Espn)).Should().Be(1);
    }
}
