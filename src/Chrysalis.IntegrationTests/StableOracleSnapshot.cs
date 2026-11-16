namespace Chrysalis.IntegrationTests;

internal static class StableOracleSnapshot
{
    internal static async Task<(TSnapshot Expected, TResult Actual)> CompareAsync<TSnapshot, TResult>(
        Func<CancellationToken, Task<TSnapshot>> readOracle,
        Func<TSnapshot, TSnapshot, bool> equivalent,
        Func<TSnapshot, CancellationToken, Task<TResult>> readSubject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(readOracle);
        ArgumentNullException.ThrowIfNull(equivalent);
        ArgumentNullException.ThrowIfNull(readSubject);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TSnapshot before = await readOracle(cancellationToken);
            TSnapshot ready = await readOracle(cancellationToken);
            if (!equivalent(before, ready))
            {
                continue;
            }
            // A subject failure is genuine only after the independent oracle precondition holds.
            TResult actual = await readSubject(ready, cancellationToken);
            TSnapshot after = await readOracle(cancellationToken);
            if (equivalent(ready, after))
            {
                return (ready, actual);
            }
        }
        throw new InvalidOperationException("Oracle precondition/comparison did not stabilize before and after the subject within three bounded attempts.");
    }
}
