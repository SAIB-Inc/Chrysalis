using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Tx.Cli.Templates.Models;

[CborSerializable]
public partial record MintOutputIndices(
    List<ulong> Indices
) : CborBase;