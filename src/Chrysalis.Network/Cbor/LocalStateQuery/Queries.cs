using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

public static class Queries
{
    public static CborBase QueryTip => new GetLedgerTipQuery(new(3));
}

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public record GetLedgerTipQuery([CborIndex(0)] CborUlong Idx) : CborBase;