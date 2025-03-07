using Chrysalis.Cbor.Attributes;
using Chrysalis.Cbor.Cardano.Types.Primitives;
using Chrysalis.Cbor.Serialization.Converters.Custom;
using Chrysalis.Cbor.Serialization.Converters.Primitives;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Custom;
using Chrysalis.Cbor.Types.Primitives;

namespace Chrysalis.Cbor.Cardano.Sundae.Types.Common;

[CborConverter(typeof(UnionConverter))]
public abstract record MultisigScript : CborBase;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 0)]
public record Signature(CborBytes KeyHash) : MultisigScript;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 1)]
public record AllOf(CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 2)]
public record AnyOf(CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 3)]
public record AtLeast(CborUlong Required, CborIndefList<MultisigScript> Scripts) : MultisigScript;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 4)]
public record Before(PosixTime Time) : MultisigScript;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 5)]
public record After(PosixTime Time) : MultisigScript;

[CborConverter(typeof(ConstrConverter))]
[CborOptions(Index = 6)]
public record Script(CborBytes ScriptHash) : MultisigScript;