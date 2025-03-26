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
    public static BlockQuery CreateBlockQuery(CborBase query) => new(0, new BlockQuery(0, new BlockQuery((int)QueryEra.Conway, query)));
    public static BlockQuery GetCurrentEra => new(0, new BlockQuery(2, null));
    public static BlockQuery GetTip => CreateBlockQuery(new BlockQuery(0, null));
    public static BlockQuery GetUtxoByAddress(List<byte[]> addresses) => CreateBlockQuery(new UtxoByAddressQuery(6, new Addresses(addresses)));
    public static BlockQuery GetUtxoByTxIns(List<TransactionInput> txIns) => CreateBlockQuery(new UtxoByTxInQuery(15, new(txIns)));
}

[CborSerializable]
[CborList]
public partial record BasicQuery([CborOrder(0)] ulong Idx) : CborBase;

[CborSerializable]
[CborList]
public partial record BlockQuery(
    [CborOrder(0)] int Query,
    [CborOrder(1)] CborBase? InnerQuery
) : CborBase;

[CborSerializable]
[CborList]
public partial record UtxoByAddressQuery(
    [CborOrder(0)] ulong Idx,
    [CborOrder(1)] Addresses Addresses
) : CborBase;

[CborSerializable]
[CborList]
public partial record UtxoByTxInQuery(
    [CborOrder(0)] ulong Idx,
    [CborOrder(1)] CborDefList<TransactionInput> TxIns
) : CborBase;

[CborSerializable]
[CborList]
public partial record Addresses(List<byte[]> Addrs) : CborBase;