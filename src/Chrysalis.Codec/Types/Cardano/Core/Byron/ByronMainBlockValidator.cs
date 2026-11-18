using Chrysalis.Codec.Serialization;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Types.Cardano.Core.Byron;

/// <summary>
/// Validates that a deserialized ByronMainBlock is actually a main block
/// (not an EBB). The discriminator is the header's BodyProof field:
/// main blocks have a CBOR array (ByronBlockProof), EBBs have a bytestring hash.
/// </summary>
public readonly record struct ByronMainBlockValidator : ICborValidator<ByronMainBlock>
{
    public bool Validate(ByronMainBlock input)
    {
        CborReader reader = new(input._field0.Span);
        reader.ReadBeginArray();
        _ = reader.ReadSize();
        reader.SkipDataItem();
        reader.SkipDataItem();
        if (!reader.Buffer.IsEmpty && (reader.Buffer[0] >> 5) == 6)
        {
            _ = reader.TryReadSemanticTag(out _);
        }
        return !reader.Buffer.IsEmpty && (reader.Buffer[0] >> 5) == 4;
    }
}
