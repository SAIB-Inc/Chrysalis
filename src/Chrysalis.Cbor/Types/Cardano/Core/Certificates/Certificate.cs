using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Cardano.Core.Governance;

namespace Chrysalis.Cbor.Types.Cardano.Core.Certificates;

/// <summary>
/// Represents a Cardano certificate used for staking, delegation, and governance operations.
/// </summary>
[CborSerializable]
[CborUnion]
public abstract partial record Certificate : CborBase { }

/// <summary>
/// Represents a stake key registration certificate.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="StakeCredential">The stake credential being registered.</param>
[CborSerializable]
[CborUnionCase(0)]
[CborList]
public partial record StakeRegistration(
   [CborOrder(0)] int Tag,
   [CborOrder(1)] Credential StakeCredential
) : Certificate;

/// <summary>
/// Represents a stake key deregistration certificate.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="StakeCredential">The stake credential being deregistered.</param>
[CborSerializable]
[CborUnionCase(1)]
[CborList]
public partial record StakeDeregistration(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential
) : Certificate;

/// <summary>
/// Represents a stake delegation certificate to a specific pool.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="StakeCredential">The stake credential being delegated.</param>
/// <param name="PoolKeyHash">The hash of the target pool's key.</param>
[CborSerializable]
[CborUnionCase(2)]
[CborList]
public partial record StakeDelegation(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] ReadOnlyMemory<byte> PoolKeyHash
) : Certificate;

/// <summary>
/// Represents a stake pool registration certificate with all pool parameters.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="Operator">The pool operator key hash.</param>
/// <param name="VrfKeyHash">The VRF verification key hash.</param>
/// <param name="Pledge">The pool pledge amount in lovelace.</param>
/// <param name="Cost">The pool fixed cost per epoch in lovelace.</param>
/// <param name="Margin">The pool margin as a rational number.</param>
/// <param name="RewardAccount">The pool reward account.</param>
/// <param name="PoolOwners">The list of pool owner key hashes.</param>
/// <param name="Relay">The list of pool relays.</param>
/// <param name="PoolMetadata">The optional pool metadata reference.</param>
[CborSerializable]
[CborUnionCase(3)]
[CborList]
public partial record PoolRegistration(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ReadOnlyMemory<byte> Operator,
    [CborOrder(2)] ReadOnlyMemory<byte> VrfKeyHash,
    [CborOrder(3)] ulong Pledge,
    [CborOrder(4)] ulong Cost,
    [CborOrder(5)] CborRationalNumber Margin,
    [CborOrder(6)] RewardAccount RewardAccount,
    [CborOrder(7)] CborMaybeIndefList<ReadOnlyMemory<byte>> PoolOwners,
    [CborOrder(8)] CborMaybeIndefList<Relay> Relay,
    [CborOrder(9)][CborNullable] PoolMetadata? PoolMetadata
) : Certificate;

/// <summary>
/// Represents a stake pool retirement certificate.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="PoolKeyHash">The hash of the pool's key being retired.</param>
/// <param name="EpochNo">The epoch number at which the pool will be retired.</param>
[CborSerializable]
[CborUnionCase(4)]
[CborList]
public partial record PoolRetirement(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ReadOnlyMemory<byte> PoolKeyHash,
    [CborOrder(2)] ulong EpochNo
) : Certificate;

/// <summary>
/// Represents a Conway-era registration certificate with a deposit.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="StakeCredential">The stake credential being registered.</param>
/// <param name="Coin">The deposit amount in lovelace.</param>
[CborSerializable]
[CborUnionCase(7)]
[CborList]
public partial record RegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] ulong Coin
) : Certificate;

/// <summary>
/// Represents a Conway-era unregistration certificate with deposit refund.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="StakeCredential">The stake credential being unregistered.</param>
/// <param name="Coin">The deposit refund amount in lovelace.</param>
[CborSerializable]
[CborUnionCase(8)]
[CborList]
public partial record UnRegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] ulong Coin
) : Certificate;

/// <summary>
/// Represents a vote delegation certificate to a DRep.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="StakeCredential">The stake credential delegating voting power.</param>
/// <param name="DRep">The target DRep for vote delegation.</param>
[CborSerializable]
[CborUnionCase(9)]
[CborList]
public partial record VoteDelegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] DRep DRep
) : Certificate;

/// <summary>
/// Represents a combined stake and vote delegation certificate.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="StakeCredential">The stake credential being delegated.</param>
/// <param name="PoolKeyHash">The hash of the target pool's key.</param>
/// <param name="DRep">The target DRep for vote delegation.</param>
[CborSerializable]
[CborUnionCase(10)]
[CborList]
public partial record StakeVoteDelegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] ReadOnlyMemory<byte> PoolKeyHash,
    [CborOrder(3)] DRep DRep
) : Certificate;

/// <summary>
/// Represents a combined stake registration and delegation certificate.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="StakeCredential">The stake credential being registered and delegated.</param>
/// <param name="PoolKeyHash">The hash of the target pool's key.</param>
/// <param name="Coin">The deposit amount in lovelace.</param>
[CborSerializable]
[CborUnionCase(11)]
[CborList]
public partial record StakeRegDelegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] ReadOnlyMemory<byte> PoolKeyHash,
    [CborOrder(3)] ulong Coin
) : Certificate;

/// <summary>
/// Represents a combined vote registration and delegation certificate.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="StakeCredential">The stake credential being registered for voting.</param>
/// <param name="DRep">The target DRep for vote delegation.</param>
/// <param name="Coin">The deposit amount in lovelace.</param>
[CborSerializable]
[CborUnionCase(12)]
[CborList]
public partial record VoteRegDelegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] DRep DRep,
    [CborOrder(3)] ulong Coin
) : Certificate;

/// <summary>
/// Represents a combined stake and vote registration and delegation certificate.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="StakeCredential">The stake credential being registered and delegated.</param>
/// <param name="PoolKeyHash">The hash of the target pool's key.</param>
/// <param name="Drep">The target DRep for vote delegation.</param>
/// <param name="Coin">The deposit amount in lovelace.</param>
[CborSerializable]
[CborUnionCase(13)]
[CborList]
public partial record StakeVoteRegDelegCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential StakeCredential,
    [CborOrder(2)] ReadOnlyMemory<byte> PoolKeyHash,
    [CborOrder(3)] DRep Drep,
    [CborOrder(4)] ulong Coin
) : Certificate;

/// <summary>
/// Represents a certificate authorizing a committee hot credential from a cold credential.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="CommitteeColdCredential">The cold credential of the committee member.</param>
/// <param name="CommitteeHotCredential">The hot credential being authorized.</param>
[CborSerializable]
[CborUnionCase(14)]
[CborList]
public partial record AuthCommitteeHotCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential CommitteeColdCredential,
    [CborOrder(2)] Credential CommitteeHotCredential
) : Certificate;

/// <summary>
/// Represents a certificate for a committee member resigning their cold credential.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="CommitteeColdCredential">The cold credential of the resigning committee member.</param>
/// <param name="Anchor">The optional anchor with resignation metadata.</param>
[CborSerializable]
[CborUnionCase(15)]
[CborList]
public partial record ResignCommitteeColdCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ReadOnlyMemory<byte> CommitteeColdCredential,
    [CborOrder(2)] Anchor? Anchor
) : Certificate;

/// <summary>
/// Represents a DRep registration certificate.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="DRepCredential">The credential of the DRep being registered.</param>
/// <param name="Coin">The deposit amount in lovelace.</param>
/// <param name="Anchor">The optional anchor with DRep metadata.</param>
[CborSerializable]
[CborUnionCase(16)]
[CborList]
public partial record RegDrepCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential DRepCredential,
    [CborOrder(2)] ulong Coin,
    [CborOrder(3)][CborNullable] Anchor? Anchor
) : Certificate;

/// <summary>
/// Represents a DRep unregistration certificate with deposit refund.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="DrepCredential">The credential of the DRep being unregistered.</param>
/// <param name="Coin">The deposit refund amount in lovelace.</param>
[CborSerializable]
[CborUnionCase(17)]
[CborList]
public partial record UnRegDrepCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential DrepCredential,
    [CborOrder(2)] ulong Coin
) : Certificate;

/// <summary>
/// Represents a DRep update certificate for changing metadata.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="DrepCredential">The credential of the DRep being updated.</param>
/// <param name="Anchor">The optional new anchor with updated DRep metadata.</param>
[CborSerializable]
[CborUnionCase(18)]
[CborList]
public partial record UpdateDrepCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] Credential DrepCredential,
    [CborOrder(2)][CborNullable] Anchor? Anchor
) : Certificate;

/// <summary>
/// Represents a genesis key delegation certificate.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="GenesisHash">The genesis key hash.</param>
/// <param name="GenesisDelegateHash">The genesis delegate key hash.</param>
/// <param name="VrfKeyHash">The VRF verification key hash.</param>
[CborSerializable]
[CborUnionCase(5)]
[CborList]
public partial record GenesisKeyDelegation(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] ReadOnlyMemory<byte> GenesisHash,
    [CborOrder(2)] ReadOnlyMemory<byte> GenesisDelegateHash,
    [CborOrder(3)] ReadOnlyMemory<byte> VrfKeyHash
) : Certificate;

/// <summary>
/// Represents a move instantaneous rewards certificate.
/// </summary>
/// <param name="Tag">The certificate tag value.</param>
/// <param name="MoveInstantaneousReward">The move instantaneous reward details.</param>
[CborSerializable]
[CborUnionCase(6)]
[CborList]
public partial record MoveInstantaneousRewardsCert(
    [CborOrder(0)] int Tag,
    [CborOrder(1)] MoveInstantaneousReward MoveInstantaneousReward
) : Certificate;
