using Chrysalis.Tx.Models;

namespace Chrysalis.Tx.Cli;

public record CreateOrderParams(
    string OwnerAddress,
    ulong LovelaceAmount,
    long PriceNum,
    long PriceDen,
    string ContractAddress,
    string ChangeAddress
) : ITransactionParameters
{
    public Dictionary<string, (string address, bool isChange)> Parties { get; set; } = new()
    {
        { "change", (ChangeAddress, true) },
        { "contract", (ContractAddress, false) }
    };
}
