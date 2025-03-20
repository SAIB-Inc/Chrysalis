using Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.Governance.DRep;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Body.Certificate;

public static class DRepExtensions
{
    public static int Tag(this DRep self) =>
        self switch
        {
            DRepAddrKeyHash dRepAddrKeyHash => dRepAddrKeyHash.Tag,
            DRepScriptHash dRepScriptHash => dRepScriptHash.Tag,
            Abstain abstain => abstain.Tag,
            DRepNoConfidence dRepNoConfidence => dRepNoConfidence.Tag,
            _ => throw new NotImplementedException()
        };

    public static byte[]? KeyHash(this DRep self) =>
        self switch
        {
            DRepAddrKeyHash dRepAddrKeyHash => dRepAddrKeyHash.AddrKeyHash,
            DRepScriptHash dRepScriptHash => dRepScriptHash.ScriptHash,
            _ => null
        };
}