using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;

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
    public static QueryReq GetCurrentEra => new BaseQuery(0, new BaseQuery(2, new GlobalQuery(1)));
    public static QueryReq GetTip => CreateBlockQuery(new GlobalQuery(0));
    public static QueryReq GetCurrentProtocolParams => CreateBlockQuery(new GlobalQuery(3));

    public static QueryReq CreateBlockQuery(QueryReq query) => new BaseQuery(0, new BaseQuery(0, new BaseQuery((int)QueryEra.Conway, query)));
    public static QueryReq GetUtxoByAddress(List<byte[]> addresses) => CreateBlockQuery(new UtxoByAddressQuery(6, new(addresses)));
    public static QueryReq GetUtxoByTxIns(List<TransactionInput> txIns) => CreateBlockQuery(new UtxoByTxInQuery(15, new(txIns)));
}


[CborSerializable]
[CborUnion]
public abstract partial record QueryReq : CborBase;

[CborSerializable]
[CborList]
public partial record BaseQuery(
    [CborOrder(0)] int Query,
    [CborOrder(1)] QueryReq? InnerQuery
) : QueryReq;

[CborSerializable]
[CborList]
public partial record UtxoByAddressQuery(
    [CborOrder(0)] ulong Idx,
    [CborOrder(1)] CborDefList<byte[]> Addresses
) : QueryReq;

[CborSerializable]
[CborList]
public partial record UtxoByTxInQuery(
    [CborOrder(0)] ulong Idx,
    [CborOrder(1)] CborDefList<TransactionInput> TxIns
) : QueryReq;

[CborSerializable]
[CborList]
public partial record GlobalQuery([CborOrder(0)] ulong Query) : QueryReq;
