using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Common;

[CborSerializable]
[CborUnion]
public abstract partial record Script : CborBase { }

[CborSerializable]
[CborList]
public partial record MultiSigScript(
    [CborOrder(0)] Value0 Version,
    [CborOrder(1)] NativeScript Script
) : Script;

[CborSerializable]
[CborList]
public partial record PlutusV1Script(
    [CborOrder(0)] Value1 Version,
    [CborOrder(1)] byte[] ScriptBytes
) : Script;

[CborSerializable]
[CborList]
public partial record PlutusV2Script(
    [CborOrder(0)] Value2 Version,
    [CborOrder(1)] byte[] ScriptBytes
) : Script;

[CborSerializable]
[CborList]
public partial record PlutusV3Script(
    [CborOrder(0)] Value3 Version,
    [CborOrder(1)] byte[] ScriptBytes
) : Script;

