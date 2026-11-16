using System.Text.Json;

namespace Chrysalis.IntegrationTests;

internal sealed class FundedReservation : IAsyncDisposable
{
    private readonly FileStream _receipt;

    private FundedReservation(string path)
    {
        _receipt = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
    }

    internal static async Task<FundedReservation> CreateAsync(string directory, CancellationToken cancellationToken)
    {
        string path = Path.Combine(directory, "funded-self-transfer-receipt.json");
        // Atomic across processes, before any key read, input selection or build. Never delete on failure.
        FundedReservation reservation = new(path);
        try
        {
            await reservation.RecordAsync(new { status = "reserved-before-preflight", reserved_utc = DateTimeOffset.UtcNow }, cancellationToken);
            return reservation;
        }
        catch
        {
            await reservation.DisposeAsync();
            throw;
        }
    }

    internal async Task RecordAsync<T>(T publicState, CancellationToken cancellationToken)
    {
        _receipt.Position = 0;
        _receipt.SetLength(0);
        await JsonSerializer.SerializeAsync(_receipt, publicState, cancellationToken: cancellationToken);
        await _receipt.FlushAsync(cancellationToken);
    }

    public ValueTask DisposeAsync() => _receipt.DisposeAsync();
}
