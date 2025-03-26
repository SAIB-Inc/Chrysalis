using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborUnion]
public abstract partial record TransactionMetadatum : CborBase
{
}

[CborSerializable]
public partial record MetadatumMap(
    Dictionary<TransactionMetadatum, TransactionMetadatum> Value
) : TransactionMetadatum;

[CborSerializable]
public partial record MetadatumList(
    List<TransactionMetadatum> Value
) : TransactionMetadatum;


[CborSerializable]
public partial record MetadatumBytes(byte[] Value) : TransactionMetadatum;


[CborSerializable]
public partial record MetadataText(string Value) : TransactionMetadatum;


[CborSerializable]
[CborUnion]
public abstract partial record MetadatumInt : TransactionMetadatum
{
}

[CborSerializable]
public partial record MetadatumIntLong(long Value) : MetadatumInt;


[CborSerializable]
public partial record MetadatumIntUlong(ulong Value) : MetadatumInt;