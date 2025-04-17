using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Tx.Cli.Templates.Models;

[CborSerializable]
[CborUnion]
public abstract partial record Action : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record BorrowAction(ActionParams ActionParams) : Action;

[CborSerializable]
[CborConstr(1)]
public partial record ForecloseAction(ActionParams ActionParams) : Action;

[CborSerializable]
[CborConstr(2)]
public partial record RepayAction(ActionParams ActionParams) : Action;

[CborSerializable]
[CborConstr(3)]
public partial record ClaimAction(ActionParams ActionParams) : Action;

[CborSerializable]
[CborConstr(4)]
public partial record CancelAction(ActionParams ActionParams) : Action;

[CborSerializable]
[CborConstr(0)]
public partial record ActionParams(
    [CborOrder(0)]
    InputIndices InputIndices,

    [CborOrder(1)]
    Option<int> GlobalProtocolParamsRefIndex,

    [CborOrder(2)]
    Option<int> PoolProtocolParamsRefIndex,

    [CborOrder(3)]
    OutputIndices OutputIndices,

    [CborOrder(4)]
    LevvyType LevvyType
) : CborBase;


[CborSerializable]
[CborConstr(0)]
public partial record InputIndices(
    [CborOrder(0)]
    int SelfInputIndex,

    [CborOrder(1)]
    Option<int> NftInputIndex
) : CborBase;


[CborSerializable]
[CborConstr(0)]
public partial record OutputIndices(
    [CborOrder(0)]
    int SelfOutputIndex,

    [CborOrder(1)]
    Option<int> FeeOutputIndex,

    [CborOrder(2)]
    Option<int> ChangeOutputIndex,

    [CborOrder(3)]
    Option<int> NftOutputIndex
) : CborBase;