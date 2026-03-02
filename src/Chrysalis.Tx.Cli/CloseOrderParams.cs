using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli;

public record CloseOrderParams(
    string ScriptUtxoTxHash,
    ulong ScriptUtxoIndex,
    string ScriptAddress,
    string DeployUtxoTxHash,
    ulong DeployUtxoIndex,
    string DeployAddress,
    string OwnerAddress,
    string ChangeAddress
) : ITransactionParameters
{
    public Dictionary<string, (string address, bool isChange)> Parties { get; set; } = new()
    {
        { "change", (ChangeAddress, true) },
        { "scriptAddress", (ScriptAddress, false) },
        { "deployAddress", (DeployAddress, false) },
        { "owner", (OwnerAddress, false) }
    };
}
