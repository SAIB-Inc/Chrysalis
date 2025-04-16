using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Tx.Cli.Templates.Models;

[CborSerializable]
[CborUnion]
public abstract partial record LevvyIdentifier : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record Multisig(MultisigScript MultiSigScript) : LevvyIdentifier;

[CborSerializable]
[CborConstr(1)]
public partial record NftPosition(Subject Signature) : LevvyIdentifier;