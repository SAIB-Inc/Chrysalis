namespace Chrysalis.Tx.Providers.UtxoRpcTests;

/// <summary>
/// Dolos 1.6.3 gaps: pallas-utxorpc v1.4.0 src/shared.rs:986-1096 omits
/// pool_retirement_epoch_bound (set from v1.5.0-rc.1). The other values match
/// https://github.com/txpipe/dolos/issues/1378, fixed for 1.6.4 by
/// https://github.com/txpipe/dolos/pull/1575. Tests assert, never correct, them.
/// </summary>
internal static class DolosGaps
{
    internal static readonly (string Key, string? Part, ulong? Value)[] Values =
    [
        ("poolRetireMaxEpoch", null, null),
        ("minPoolCost", null, 170_000_000),
        ("maxTxExecutionUnits", "memory", 16_500_000),
        ("maxBlockExecutionUnits", "memory", 72_000_000)
    ];
}
