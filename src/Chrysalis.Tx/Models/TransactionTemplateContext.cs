using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.TransactionWitness;
using ChrysalisWallet = Chrysalis.Wallet.Models.Addresses;

namespace Chrysalis.Tx.Models;
public record TransactionTemplateContext
{
    public Dictionary<string, string> Parties { get; set; } = [];
    public ChrysalisWallet.Address ChangeAddress { get; set; } = null!;
    public List<string> InputAddresses { get; set; } = [];
    public Dictionary<string, TransactionInput> InputsById { get; set; } = [];
    public Dictionary<string, Dictionary<string, int>> AssociationsByInputId { get; set; } = [];
    public Dictionary<int, Dictionary<string, int>> IndexedAssociations { get; set; } = [];
    public Dictionary<RedeemerKey, RedeemerValue> Redeemers { get; set; } = [];
    public List<TransactionInput> SpecifiedInputs { get; set; } = [];
    public List<Value> RequiredAmount { get; set; } = [];
    public List<ResolvedInput>? ChangeAddressUtxos { get; set; }
    public ResolvedInput? FeeInput { get; set; }
    public TransactionInput? ReferenceInput { get; set; }
    public byte[] ScriptCborBytes { get; set; } = [];
    public bool IsSmartContractTx { get; set; }
    public ulong MinimumLovelace { get; set; }
}