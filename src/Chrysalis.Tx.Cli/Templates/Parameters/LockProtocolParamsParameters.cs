using Chrysalis.Tx.Cli.Templates.Models;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli.Templates.Parameters;

public record LockProtocolParamsParameters(string ValidatorAddress, string Policy, string AssetName, LevvyGlobalProtocolParams? GlobalParams, LevvyPoolProtocolParams? PoolParams) : ITransactionParameters
{
    public Dictionary<string, (string address, bool isChange)> Parties { get; set; } = new() {
        { "validator", (ValidatorAddress, false) },
    };
}
