using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Script;

[CborSerializable]
[CborUnion]
public abstract partial record PlutusData : CborBase<PlutusData>
{
    [CborSerializable]
    [CborConstr(0)]
    public partial record PlutusConstr(List<PlutusData> PlutusData) : PlutusData;


    [CborSerializable]
    public partial record PlutusMap(Dictionary<PlutusData, PlutusData> PlutusData) : PlutusData;

    [CborSerializable]
    public partial record PlutusList(List<PlutusData> PlutusData) : PlutusData;


    [CborSerializable]
    [CborUnion]
    public abstract partial record PlutusBigInt : PlutusData
    {
        [CborSerializable]
        [CborUnion]
        public abstract partial record PlutusInt : PlutusBigInt
        {
            [CborSerializable]
            public partial record PlutusInt64(long Value) : PlutusInt;

            [CborSerializable]
            public partial record PlutusUint64(ulong Value) : PlutusInt;
        }

        [CborSerializable]
        public partial record PlutusBigUint([CborSize(64)] byte[] Value) : PlutusBigInt;


        [CborSerializable]
        public partial record PlutusBigNint([CborSize(64)] byte[] Value) : PlutusBigInt;
    }

    [CborSerializable]
    public partial record PlutusBoundedBytes([CborSize(64)] byte[] Value) : PlutusData;

}

