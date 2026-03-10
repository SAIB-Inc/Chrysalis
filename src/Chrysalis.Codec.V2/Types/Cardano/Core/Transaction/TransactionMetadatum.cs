using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborUnion]
public partial interface ITransactionMetadatum : ICborType;

[CborSerializable]
public readonly partial record struct MetadatumMap : ITransactionMetadatum
{
    public partial Dictionary<ITransactionMetadatum, ITransactionMetadatum> Value { get; }
}

[CborSerializable]
public readonly partial record struct MetadatumList : ITransactionMetadatum
{
    public partial List<ITransactionMetadatum> Value { get; }
}

[CborSerializable]
public readonly partial record struct MetadatumBytes : ITransactionMetadatum
{
    public partial byte[] Value { get; }
}

[CborSerializable]
public readonly partial record struct MetadataText : ITransactionMetadatum
{
    public partial string Value { get; }
}

[CborSerializable]
[CborUnion]
public partial interface IMetadatumInt : ITransactionMetadatum;

[CborSerializable]
public readonly partial record struct MetadatumIntLong : IMetadatumInt
{
    public partial long Value { get; }
}

[CborSerializable]
public readonly partial record struct MetadatumIntUlong : IMetadatumInt
{
    public partial ulong Value { get; }
}
