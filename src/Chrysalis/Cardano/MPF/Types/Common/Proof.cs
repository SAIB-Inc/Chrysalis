
using Chrysalis.Cardano.Core.Types.Primitives;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.MPF.Types.Common;

[CborConverter(typeof(UnionConverter))]
public record ProofStep : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Branch(
    [CborProperty(0)]
    CborUlong Skip,

    [CborProperty(1)]
    CborBoundedBytes Neighbors
) : ProofStep;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record Fork(
    [CborProperty(0)]
    CborUlong Skip,

    [CborProperty(1)]
    Neighbor Neighbor
) : ProofStep;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(2)]
public record Leaf(
    [CborProperty(0)]
    CborUlong Skip,

    [CborProperty(1)]
    CborBoundedBytes Key,

    [CborProperty(2)]
    CborBoundedBytes Value
) : ProofStep;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Neighbor(
    [CborProperty(0)]
    CborUlong Nibble,

    [CborProperty(1)]
    CborBoundedBytes Prefix,

    [CborProperty(2)]
    CborBoundedBytes Root
) : ProofStep;

[CborConverter(typeof(ListConverter))]
public record Proof(CborIndefList<ProofStep> ProofSteps) : CborBase;