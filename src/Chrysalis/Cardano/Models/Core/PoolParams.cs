using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Core;

[CborSerializable(CborType.Map)]
public record PoolParams(
    [CborProperty(0)] CborBytes PoolKeyHash, 
    [CborProperty(1)] CborBytes VrfKeyHash,
    [CborProperty(2)] CborUlong Pledge,    
    [CborProperty(3)] CborUlong Cost,
    [CborProperty(4)] CborUlong UnitInterval,
    [CborProperty(5)] RewardAccount RewardAccount, //@TODO: Clarify
    [CborProperty(6)] CborIndefiniteList<CborBytes> PoolOwners,
    [CborProperty(7)] CborDefiniteList<Relay> Relay,
    [CborProperty(8)] PoolMetadata? PoolMetadata    
) : ICbor;