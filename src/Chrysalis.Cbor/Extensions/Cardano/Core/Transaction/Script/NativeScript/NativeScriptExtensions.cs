using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using CNativeScript = Chrysalis.Cbor.Types.Cardano.Core.Common.NativeScript;

namespace Chrysalis.Cbor.Extensions.Block.Transaction.Script.NativeScript;

public static class NativeScriptExtensions
{
    public static int Tag(this CNativeScript self) =>
        self switch
        {
            ScriptPubKey scriptPubKey => scriptPubKey.Tag,
            ScriptAll scriptAll => scriptAll.Tag,
            ScriptAny scriptAny => scriptAny.Tag,
            ScriptNOfK scriptNOfK => scriptNOfK.Tag,
            InvalidBefore invalidBefore => invalidBefore.Tag,
            InvalidHereafter invalidHereAfter => invalidHereAfter.Tag,
            _ => throw new NotSupportedException()
        };

    public static byte[] AddressKeyHash(this CNativeScript self) =>
        self switch
        {
            ScriptPubKey scriptPubKey => scriptPubKey.AddrKeyHash,
            _ => throw new NotSupportedException()
        };

    public static IEnumerable<CNativeScript> Scripts(this CNativeScript self) =>
        self switch
        {
            ScriptAll scriptAll => scriptAll.Scripts switch
            {
                CborIndefList<CNativeScript> defList => defList.Value,
                _ => []
            },
            ScriptAny scriptAny => scriptAny.Scripts switch
            {
                CborDefList<CNativeScript> defList => defList.Value,
                _ => []
            },
            ScriptNOfK scriptNOfK => scriptNOfK.Scripts switch
            {
                CborDefList<CNativeScript> defList => defList.Value,
                _ => []
            },
            _ => []
        };

    public static int N(this CNativeScript self) =>
        self switch
        {
            ScriptNOfK scriptNOfK => scriptNOfK.N,
            _ => throw new NotSupportedException()
        };

    public static ulong Slot(this CNativeScript self) =>
        self switch
        {
            InvalidBefore invalidBefore => invalidBefore.Slot,
            InvalidHereafter invalidHereAfter => invalidHereAfter.Slot,
            _ => throw new NotSupportedException()
        };
}