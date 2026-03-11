using Chrysalis.Codec.Serialization;

namespace Chrysalis.Codec.Types.Cardano.Core.Byron;

/// <summary>
/// Validates that a deserialized ByronMainBlock is actually a main block
/// (not an EBB). The discriminator is the header's BodyProof field:
/// main blocks have a CBOR array (ByronBlockProof), EBBs have a bytestring hash.
/// </summary>
public class ByronMainBlockValidator : ICborValidator<ByronMainBlock>
{
    public bool Validate(ByronMainBlock input)
    {
        // BodyProof is field 2 of ByronBlockHead (a lazy struct).
        // Check the first byte's CBOR major type: 4 = array, 2 = bytes.
        ReadOnlyMemory<byte> bodyProofSlice = input.Header._field2;
        if (bodyProofSlice.Length == 0)
        {
            return false;
        }

        int majorType = bodyProofSlice.Span[0] >> 5;
        return majorType == 4; // CBOR array
    }
}
