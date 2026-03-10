using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;

namespace Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;

[CborSerializable]
[CborUnion]
public partial interface ITransactionWitnessSet : ICborType;

[CborSerializable]
[CborMap]
public readonly partial record struct AlonzoTransactionWitnessSet : ITransactionWitnessSet
{
    [CborProperty(0)] public partial ICborMaybeIndefList<VKeyWitness>? VKeyWitnesses { get; }
    [CborProperty(1)] public partial ICborMaybeIndefList<INativeScript>? NativeScripts { get; }
    [CborProperty(2)] public partial ICborMaybeIndefList<BootstrapWitness>? BootstrapWitnesses { get; }
    [CborProperty(3)] public partial ICborMaybeIndefList<ReadOnlyMemory<byte>>? PlutusV1Scripts { get; }
    [CborProperty(4)] public partial ICborMaybeIndefList<IPlutusData>? PlutusDataSet { get; }
    [CborProperty(5)] public partial IRedeemers? Redeemers { get; }
}

[CborSerializable]
[CborMap]
public readonly partial record struct PostAlonzoTransactionWitnessSet : ITransactionWitnessSet
{
    [CborProperty(0)] public partial ICborMaybeIndefList<VKeyWitness>? VKeyWitnesses { get; }
    [CborProperty(1)] public partial ICborMaybeIndefList<INativeScript>? NativeScripts { get; }
    [CborProperty(2)] public partial ICborMaybeIndefList<BootstrapWitness>? BootstrapWitnesses { get; }
    [CborProperty(3)] public partial ICborMaybeIndefList<ReadOnlyMemory<byte>>? PlutusV1Scripts { get; }
    [CborProperty(4)] public partial ICborMaybeIndefList<IPlutusData>? PlutusDataSet { get; }
    [CborProperty(5)] public partial IRedeemers? Redeemers { get; }
    [CborProperty(6)] public partial ICborMaybeIndefList<ReadOnlyMemory<byte>>? PlutusV2Scripts { get; }
    [CborProperty(7)] public partial ICborMaybeIndefList<ReadOnlyMemory<byte>>? PlutusV3Scripts { get; }
}
