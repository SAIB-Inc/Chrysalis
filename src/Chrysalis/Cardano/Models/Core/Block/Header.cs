using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core.Block;

[CborSerializable(CborType.Union)]
[CborUnionTypes([
    typeof(AlonzoBlockHeader),
    typeof(EbbHead),
])]
public record BlockHeader : ICbor;

[CborSerializable(CborType.List)]
public record AlonzoBlockHeader(
    [CborProperty(0)] BlockHeaderBody HeaderBody,
    [CborProperty(1)] CborBytes BodySignature
) : BlockHeader;

[CborSerializable(CborType.List)]
public record EbbHead(
    [CborProperty(0)] CborUlong ProtocolMagic,
    [CborProperty(1)] CborBytes PrevBlock,
    [CborProperty(2)] CborBytes BodyProof,    
    [CborProperty(3)] EbbCons ConsensusData, 
    [CborProperty(4)] CborDefiniteList<CborMap<ICbor,ICbor>> ExtraData //attributes = {* any => any}
) : BlockHeader;

