using Chrysalis.Cbor.Types.Cardano.Core.Governance;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Certificates;

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