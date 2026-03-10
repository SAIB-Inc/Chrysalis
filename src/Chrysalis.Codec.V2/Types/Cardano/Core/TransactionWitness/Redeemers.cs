using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;
using Chrysalis.Codec.V2.Types.Cardano.Core.Common;
using Chrysalis.Codec.V2.Types.Cardano.Core.Protocol;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.TransactionWitness;

[CborSerializable]
[CborUnion]
public partial interface IRedeemers : ICborType;

[CborSerializable]
public readonly partial record struct RedeemerList : IRedeemers
{
    public partial List<RedeemerEntry> Value { get; }
}

[CborSerializable]
public readonly partial record struct RedeemerMap : IRedeemers
{
    public partial Dictionary<RedeemerKey, RedeemerValue> Value { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct RedeemerEntry : ICborType
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ulong Index { get; }
    [CborOrder(2)] public partial IPlutusData Data { get; }
    [CborOrder(3)] public partial ExUnits ExUnits { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct RedeemerKey : ICborType
{
    [CborOrder(0)] public partial int Tag { get; }
    [CborOrder(1)] public partial ulong Index { get; }
}

[CborSerializable]
[CborList]
public readonly partial record struct RedeemerValue : ICborType
{
    [CborOrder(0)] public partial IPlutusData Data { get; }
    [CborOrder(1)] public partial ExUnits ExUnits { get; }
}
