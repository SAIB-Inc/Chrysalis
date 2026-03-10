using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;
using Chrysalis.Codec.V2.Types.Cardano.Core.Common;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;

[CborSerializable]
[CborUnion]
public partial interface ITransactionOutput : ICborType;

[CborSerializable]
[CborList]
public readonly partial record struct AlonzoTransactionOutput : ITransactionOutput
{
    [CborOrder(0)] public partial Address Address { get; }
    [CborOrder(1)] public partial IValue Amount { get; }
    [CborOrder(2)] public partial ReadOnlyMemory<byte>? DatumHash { get; }
}

[CborSerializable]
[CborMap]
public readonly partial record struct PostAlonzoTransactionOutput : ITransactionOutput
{
    [CborProperty(0)] public partial Address Address { get; }
    [CborProperty(1)] public partial IValue Amount { get; }
    [CborProperty(2)] public partial IDatumOption? Datum { get; }
    [CborProperty(3)] public partial CborEncodedValue? ScriptRef { get; }
}
