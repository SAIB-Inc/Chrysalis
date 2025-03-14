using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Serialization;


using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Network.Cbor.LocalStateQuery;

public enum QueryEra
{
    Byron = 0,
    Shelley = 1,
    Allegra = 2,
    Mary = 3,
    Alonzo = 4,
    Babbage = 5,
    Conway = 6,
}

public static class RawQueries
{
    public static BlockQuery CreateBlockQuery(CborBase query)
    {
        return new BlockQuery(
            new CborUlong(0),
            new BlockQuery(
                new CborUlong(0),
                new BlockQuery(
                    // @TODO: This is currently hardcoded. We need to probably make this dynamic
                    // by querying the current era from the ledger.
                    new CborUlong((ulong)QueryEra.Conway),
                    query
                )
            )
        );
    }

    public static BlockQuery GetCurrentEra =>
        new BlockQuery(
            new CborUlong(0),
            new BlockQuery(
                new CborUlong(2),
                null
            )
        );

    public static BlockQuery GetTip => CreateBlockQuery(
        new BlockQuery(
            new CborUlong(0),
            null
        )
    );

    public static BlockQuery GetUtxoByAddress(List<byte[]> addresses)
    {
        List<CborBytes> cborAddress = [.. addresses.Select(a => new CborBytes(a))];
        return CreateBlockQuery(new UtxoByAddressQuery(new(6), new Addresses(cborAddress)));
    }

    public static BlockQuery GetUtxoByTxIns(List<TransactionInput> txIns)
    {
        return CreateBlockQuery(new UtxoByTxInQuery(new(15), new(txIns)));
    }
}

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record BasicQuery([CborIndex(0)] CborUlong Idx) : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record BlockQuery(
    [CborIndex(0)] CborBase Query,
    [CborIndex(1)] CborBase? InnerQuery
) : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record UtxoByAddressQuery(
    [CborIndex(0)] CborUlong Idx,
    [CborIndex(1)] Addresses Addresses
) : CborBase;

[CborConverter(typeof(CustomListConverter))]
[CborOptions(IsDefinite = true)]
public partial record UtxoByTxInQuery(
    [CborIndex(0)] CborUlong Idx,
    [CborIndex(1)] CborDefList<TransactionInput> TxIns
) : CborBase;

[CborConverter(typeof(ListConverter))]
[CborOptions(IsDefinite = true)]
public partial record Addresses(List<CborBytes> Addrs) : CborBase;