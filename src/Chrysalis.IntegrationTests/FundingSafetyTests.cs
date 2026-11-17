using Xunit;

namespace Chrysalis.IntegrationTests;

/// <summary>Offline funding safety regressions; no wallet is read and no transaction is built or submitted.</summary>
public sealed class FundingSafetyTests
{
    /// <summary>A configured wallet path is insufficient; only the exact explicit opt-in value enables funding.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void FundingRejectsEveryOptInValueExceptOne()
    {
        foreach (string? value in new string?[] { null, string.Empty, "0", "true", "yes", " 1", "1 " })
        {
            Assert.False(IntegrationEnvironment.IsFundedOptIn(value));
        }
        Assert.True(IntegrationEnvironment.IsFundedOptIn("1"));
    }

    /// <summary>The committed accepted record refuses spending without consulting a receipt, wallet or node.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void AcceptedGoldenRefusesSubmissionWithoutMutableState()
    {
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(DijkstraProbes.RequireNoAcceptedSubmission);
        Assert.Contains("already accepted", failure.Message, StringComparison.Ordinal);
        Assert.Contains("reviewed Golden change", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>Changing capture output cannot re-arm DijkstraNet's one-shot guard.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public void DijkstraReceiptIsIndependentOfCaptureOutput()
    {
        string? original = Environment.GetEnvironmentVariable(IntegrationEnvironment.Output);
        string scratch = Path.Combine(Path.GetTempPath(), "chrysalis-receipt-path-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable(IntegrationEnvironment.Output, Path.Combine(scratch, "first"));
            string first = FundedReservation.DijkstraDirectory;
            Environment.SetEnvironmentVariable(IntegrationEnvironment.Output, Path.Combine(scratch, "second"));
            Assert.Equal(first, FundedReservation.DijkstraDirectory);
            Assert.EndsWith(Path.Combine(".integration-artifacts", "dijkstra"), first, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(IntegrationEnvironment.Output, original);
            if (Directory.Exists(scratch)) { Directory.Delete(scratch, recursive: true); }
        }
    }

    /// <summary>Holds two simultaneous attempts before any transaction work; exactly one may reserve.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConcurrentFundedReserversHaveExactlyOneWinner()
    {
        string directory = Path.Combine(Path.GetTempPath(), "chrysalis-funding-safety-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        FundedReservation?[] reservations = [];
        try
        {
            Task<FundedReservation?> AttemptAsync() => Task.Run(async () =>
            {
                await start.Task;
                try { return await FundedReservation.CreateAsync(directory, CancellationToken.None); }
                catch (IOException) { return null; }
            });
            Task<FundedReservation?> first = AttemptAsync();
            Task<FundedReservation?> second = AttemptAsync();
            start.SetResult();
            reservations = await Task.WhenAll(first, second);
            _ = Assert.Single(reservations, reservation => reservation is not null);
        }
        finally
        {
            foreach (FundedReservation reservation in reservations.OfType<FundedReservation>())
            {
                await reservation.DisposeAsync();
            }
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>Disposing an unsuccessful attempt never erases its guard or permits a replay.</summary>
    [Fact]
    [Trait("Category", "Integration")]
    public async Task UncertainFundedAttemptKeepsItsReservation()
    {
        string directory = Path.Combine(Path.GetTempPath(), "chrysalis-funding-safety-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(directory);
        try
        {
            await using (FundedReservation reservation = await FundedReservation.CreateAsync(directory, CancellationToken.None))
            {
                Assert.True(File.Exists(Path.Combine(directory, "funded-self-transfer-receipt.json")));
            }
            _ = await Assert.ThrowsAsync<IOException>(async () =>
            {
                await using FundedReservation reservation = await FundedReservation.CreateAsync(directory, CancellationToken.None);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
