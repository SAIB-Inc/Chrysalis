
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Cli.Templates.Models;
using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli.Templates.Parameters;
public record LendParams(
    LendDatum LendDatum, 
    string ValidatorAddress
) : ITransactionParameters
{
     public Dictionary<string, (string address, bool isChange)> Parties { get; set; } = new() {
        { "validator", (ValidatorAddress, false) },
    };
}
    
