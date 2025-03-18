using Chrysalis.Cbor.Serialization.Attributes;


using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Cardano.Types.Block.Transaction.Output;

[CborSerializable]
[CborUnion]
public abstract partial record TokenBundle : CborBase<TokenBundle>
{
    [CborSerializable]
    public partial record TokenBundleOutput(Dictionary<byte[], ulong> Value) : TokenBundle;


    [CborSerializable]
    public partial record TokenBundleMint(Dictionary<byte[], long> Value) : TokenBundle;
}