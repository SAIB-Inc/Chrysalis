using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Serialization;

namespace Chrysalis.Cbor.Types.Cardano.Core.Header;

/// <summary>
/// A Cardano block header containing the header body and its KES signature.
/// </summary>
/// <param name="HeaderBody">The block header body with block metadata.</param>
/// <param name="BodySignature">The KES signature over the header body.</param>
[CborSerializable]
[CborList]
public partial record BlockHeader(
    [CborOrder(0)] BlockHeaderBody HeaderBody,
    [CborOrder(1)] byte[] BodySignature
) : CborBase, ICborPreserveRaw;
