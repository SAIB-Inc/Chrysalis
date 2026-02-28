using Chrysalis.Cbor.Serialization.Attributes;

namespace Chrysalis.Cbor.Types.Cardano.Core.Transaction;

/// <summary>
/// A metadata anchor containing a URL and its content hash, used in governance and certificates.
/// </summary>
/// <param name="AnchorUrl">The URL pointing to the metadata document.</param>
/// <param name="AnchorDataHash">The hash of the metadata document content.</param>
[CborSerializable]
[CborList]
public partial record Anchor(
    [CborOrder(0)] string AnchorUrl,
    [CborOrder(1)] byte[] AnchorDataHash
) : CborBase;
