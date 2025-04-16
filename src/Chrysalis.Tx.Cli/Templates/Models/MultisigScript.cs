using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Tx.Cli.Templates.Models;

[CborSerializable]
[CborUnion]
public abstract partial record MultisigScript : CborBase;

[CborSerializable]
[CborConstr(0)]
public partial record Signature(byte[] KeyHash) : MultisigScript;

[CborSerializable]
[CborConstr(1)]
public partial record AllOf(CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable]
[CborConstr(2)]
public partial record AnyOf(CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable]
[CborConstr(3)]
public partial record AtLeast(ulong Required, CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable]
[CborConstr(4)]
public partial record Before(PosixTime Time) : MultisigScript;

[CborSerializable]
[CborConstr(5)]
public partial record After(PosixTime Time) : MultisigScript;

[CborSerializable]
[CborConstr(6)]
public partial record Script(byte[] ScriptHash) : MultisigScript;