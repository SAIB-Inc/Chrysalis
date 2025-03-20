using CNativeScript = Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script.NativeScript;
using static Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script.NativeScript;
using Chrysalis.Cbor.Types.Custom;

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
            InvalidHereAfter invalidHereAfter => invalidHereAfter.Tag,
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
                CborMaybeIndefList<CNativeScript>.CborDefList defList => defList.Value,
                _ => []
            },
            ScriptAny scriptAny => scriptAny.Scripts switch
            {
                CborMaybeIndefList<CNativeScript>.CborDefList defList => defList.Value,
                _ => []
            },
            ScriptNOfK scriptNOfK => scriptNOfK.Scripts switch
            {
                CborMaybeIndefList<CNativeScript>.CborDefList defList => defList.Value,
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
            InvalidHereAfter invalidHereAfter => invalidHereAfter.Slot,
            _ => throw new NotSupportedException()
        };
}