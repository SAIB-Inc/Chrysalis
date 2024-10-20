using Chrysalis.Cardano.Models.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Models.Sundae;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(Signature), typeof(AllOf), typeof(AnyOf), typeof(AtLeast), typeof(Before), typeof(After), typeof(Script)])]
public record MultisigScript : RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record Signature(CborBytes KeyHash) : MultisigScript;

[CborSerializable(CborType.Constr, Index = 1)]
public record AllOf(CborIndefiniteList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable(CborType.Constr, Index = 2)]
public record AnyOf(CborIndefiniteList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable(CborType.Constr, Index = 3)]
public record AtLeast(CborUlong Required, CborIndefiniteList<MultisigScript> Scripts) : MultisigScript;

[CborSerializable(CborType.Constr, Index = 4)]
public record Before(PosixTime Time) : MultisigScript;

[CborSerializable(CborType.Constr, Index = 5)]
public record After(PosixTime Time) : MultisigScript;

[CborSerializable(CborType.Constr, Index = 6)]
public record Script(CborBytes ScriptHash) : MultisigScript;