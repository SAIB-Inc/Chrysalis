using Chrysalis.Cardano.Core.Types.Primitives;
using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Collections;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cardano.Sundae.Types.Common;

[CborConverter(typeof(UnionConverter))]
public abstract record MultisigScript : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Signature(CborBytes KeyHash) : MultisigScript;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record AllOf(CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(2)]
public record AnyOf(CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(3)]
public record AtLeast(CborUlong Required, CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(4)]
public record Before(PosixTime Time) : MultisigScript;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(5)]
public record After(PosixTime Time) : MultisigScript;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(6)]
public record Script(CborBytes ScriptHash) : MultisigScript;