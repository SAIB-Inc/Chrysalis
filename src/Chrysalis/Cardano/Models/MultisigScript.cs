using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(Signature)])]
public record MultisigScript : ICbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record Signature(CborBytes KeyHash) : MultisigScript;

[CborSerializable(CborType.Constr, Index = 1)]
public record AllOf(CborList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable(CborType.Constr, Index = 2)]
public record AnyOf(CborList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable(CborType.Constr, Index = 3)]
public record AtLeast(CborUlong Required, CborList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable(CborType.Constr, Index = 4)]
public record Before(PosixTime Time) : MultisigScript;

[CborSerializable(CborType.Constr, Index = 5)]
public record After(PosixTime Time) : MultisigScript;

[CborSerializable(CborType.Constr, Index = 6)]
public record Script(CborBytes ScriptHash) : MultisigScript;