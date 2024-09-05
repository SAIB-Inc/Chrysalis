
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
    CborInt Skip,

    [CborProperty(1)]
    CborBytes Neighbors
) : ProofStep;

[CborSerializable(CborType.Constr, Index = 1)]
public record Fork(
    [CborProperty(0)]
    CborInt Skip,

    [CborProperty(1)]
    CborBytes Neighbor
) : ProofStep;

[CborSerializable(CborType.Constr, Index = 2)]
public record Leaf(
    [CborProperty(0)]
    CborInt Skip,

    [CborProperty(1)]
    CborBytes Key,

    [CborProperty(2)]
    CborBytes Value
) : ProofStep;
