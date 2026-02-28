using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Cbor.Types.Plutus;

/// <summary>
/// A CIP-68 datum structure containing metadata, a version number, and optional extra data.
/// </summary>
/// <typeparam name="T">The type of the optional extra data field.</typeparam>
/// <param name="Metadata">The Plutus data containing the CIP-68 metadata fields.</param>
/// <param name="Version">The CIP-68 metadata version number.</param>
/// <param name="Extra">The optional extra data of type T.</param>
[CborSerializable]
[CborConstr(0)]
public partial record Cip68<T>(
    [CborOrder(0)] PlutusData Metadata,
    [CborOrder(1)] int Version,
    [CborOrder(2)] T? Extra
) : CborBase;
