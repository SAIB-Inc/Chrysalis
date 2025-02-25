using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

public static class Queries
{
    public static GetLedgerTipQuery GetLedgerTipQuery => new(new(0));
}
public record GetLedgerTipQuery([CborIndex(0)] CborUlong Idx) : CborBase;