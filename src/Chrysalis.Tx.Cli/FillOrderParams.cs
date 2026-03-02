using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli;

public record FillOrderParams(
    string ScriptUtxoTxHash,
    ulong ScriptUtxoIndex,
    string ScriptAddress,
    byte[] DatumCbor,
    ulong AmountToBuy,
    long PriceNum,
    long PriceDen,
    string DeployUtxoTxHash,
    ulong DeployUtxoIndex,
    string DeployAddress,
    string ChangeAddress
) : ITransactionParameters
{
    public Dictionary<string, (string address, bool isChange)> Parties { get; set; } = new()
    {
        { "change", (ChangeAddress, true) },
        { "scriptAddress", (ScriptAddress, false) },
        { "deployAddress", (DeployAddress, false) }
    };
}
