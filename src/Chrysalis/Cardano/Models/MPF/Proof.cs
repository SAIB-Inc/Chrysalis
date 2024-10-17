
using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Mpf;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(Branch),
    typeof(Fork),
    typeof(Leaf)
])]
public record ProofStep : ICbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record Branch(
    [CborProperty(0)]
    CborUlong Skip,

    [CborProperty(1)]
    CborBoundedBytes Neighbors
) : ProofStep;

[CborSerializable(CborType.Constr, Index = 1)]
public record Fork(
    [CborProperty(0)]
    CborUlong Skip,

    [CborProperty(1)]
    Neighbor Neighbor
) : ProofStep;

[CborSerializable(CborType.Constr, Index = 2)]
public record Leaf(
    [CborProperty(0)]
    CborUlong Skip,

    [CborProperty(1)]
    CborBoundedBytes Key,

    [CborProperty(2)]
    CborBoundedBytes Value
) : ProofStep;

[CborSerializable(CborType.Constr, Index = 0)]
public record Neighbor(
    [CborProperty(0)]
    CborUlong Nibble,

    [CborProperty(1)]
    CborBoundedBytes Prefix,

    [CborProperty(2)]
    CborBoundedBytes Root
) : ProofStep;

public record Proof(ProofStep[] ProofSteps) : CborIndefiniteList<ProofStep>(ProofSteps);