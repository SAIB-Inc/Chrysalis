using Chrysalis.Codec.V2.Serialization;
using Chrysalis.Codec.V2.Serialization.Attributes;
using Chrysalis.Codec.V2.Types.Cardano.Core.Transaction;

namespace Chrysalis.Codec.V2.Types.Cardano.Core.Governance;

[CborSerializable]
[CborList]
public readonly partial record struct Constitution : ICborType
{
    [CborOrder(0)] public partial Anchor Anchor { get; }
    [CborOrder(1)] public partial ReadOnlyMemory<byte>? GuardrailsScriptHash { get; }
}
