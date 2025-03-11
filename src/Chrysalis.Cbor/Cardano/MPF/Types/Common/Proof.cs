using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Types.Primitives;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.MPF.Types.Common;

[CborConverter(typeof(UnionConverter))]
public partial record ProofStep : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record Branch(
    [CborIndex(0)]
    CborUlong Skip,

    [CborIndex(1)]
    CborBoundedBytes Neighbors
) : ProofStep;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 1)]
public partial record Fork(
    [CborIndex(0)]
    CborUlong Skip,

    [CborIndex(1)]
    Neighbor Neighbor
) : ProofStep;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 2)]
public partial record Leaf(
    [CborIndex(0)]
    CborUlong Skip,

    [CborIndex(1)]
    CborBoundedBytes Key,

    [CborIndex(2)]
    CborBoundedBytes Value
) : ProofStep;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public partial record Neighbor(
    [CborIndex(0)]
    CborUlong Nibble,

    [CborIndex(1)]
    CborBoundedBytes Prefix,

    [CborIndex(2)]
    CborBoundedBytes Root
) : ProofStep;

[CborConverter(typeof(ListConverter))]
public partial record Proof(CborIndefList<ProofStep> ProofSteps) : CborBase;