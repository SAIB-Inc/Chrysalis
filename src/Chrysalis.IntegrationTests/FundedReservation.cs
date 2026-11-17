using System.Text.Json;

namespace Chrysalis.IntegrationTests;

internal sealed class FundedReservation : IAsyncDisposable
{
    private readonly FileStream _receipt;

    internal static string DijkstraDirectory
    {
        get
        {
            DirectoryInfo? root = new(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Chrysalis.slnx"))) { root = root.Parent; }
            return root is null ? throw new InvalidOperationException("Cannot locate the repository for the fixed Dijkstra receipt.")
                : Path.Combine(root.FullName, ".integration-artifacts", "dijkstra");
        }
    }

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
