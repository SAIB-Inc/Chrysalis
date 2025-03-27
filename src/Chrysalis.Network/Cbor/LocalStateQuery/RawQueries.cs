using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

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
    public static BlockQuery CreateBlockQuery(BlockQuery query) => new BaseBlockQuery(0, new BaseBlockQuery(0, new BaseBlockQuery((int)QueryEra.Conway, query)));
    public static BlockQuery GetCurrentEra => new BaseBlockQuery(0, new BaseBlockQuery(2, null));
    public static BlockQuery GetTip => CreateBlockQuery(new BaseBlockQuery(0, null));
    public static BlockQuery GetUtxoByAddress(List<byte[]> addresses) => CreateBlockQuery(new UtxoByAddressQuery(6, new Addresses(addresses)));
    public static BlockQuery GetUtxoByTxIns(List<TransactionInput> txIns) => CreateBlockQuery(new UtxoByTxInQuery(15, new(txIns)));
}

[CborSerializable]
[CborList]
public partial record BasicQuery([CborOrder(0)] ulong Idx) : CborBase;

[CborSerializable]
[CborUnion]
public abstract partial record BlockQuery : CborBase;

[CborSerializable]
[CborList]
public partial record BaseBlockQuery(
    [CborOrder(0)] int Query,
    [CborOrder(1)] BlockQuery? InnerQuery
) : BlockQuery;

[CborSerializable]
[CborList]
public partial record UtxoByAddressQuery(
    [CborOrder(0)] ulong Idx,
    [CborOrder(1)] Addresses Addresses
) : BlockQuery;

[CborSerializable]
[CborList]
public partial record UtxoByTxInQuery(
    [CborOrder(0)] ulong Idx,
    [CborOrder(1)] CborDefList<TransactionInput> TxIns
) : BlockQuery;

[CborSerializable]
[CborList]
public partial record Addresses(List<byte[]> Addrs) : BlockQuery;