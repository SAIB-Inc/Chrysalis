using Chrysalis.Attributes;
using Chrysalis.Converters;

namespace Chrysalis.Types.Custom.Crashr;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record ListingDatum(
  [CborProperty(0)] CborIndefiniteList<CrashrPayoutDatum> Payouts,
  [CborProperty(1)] CborBytes Owner
) : Cbor;

[CborConverter(typeof(ListConverter))]
public record CborIndefiniteList<T>(List<T> Value) : Cbor;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record CrashrPayoutDatum(
  [CborProperty(0)] Address Address,
  [CborProperty(1)] MultiAssetOutput Amount
) : Cbor;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Address(
    [CborProperty(0)] Credential PaymentCredential,
    [CborProperty(1)] Option<Inline<Credential>> StakeCredential
) : Cbor;

[CborConverter(typeof(UnionConverter))]
public abstract record Credential : Cbor;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record VerificationKey([CborProperty(0)] CborBytes VerificationKeyHash) : Credential;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record Script([CborProperty(0)] CborBytes ScriptHash) : Credential;


[CborConverter(typeof(UnionConverter))]
public abstract record Referenced<T> : Cbor;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Inline<T>(T Value) : Referenced<T>;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record Pointer(
    [CborProperty(0)]
    CborUlong SlotNumber,

    [CborProperty(1)]
    CborUlong TransactionIndex,

    [CborProperty(2)]
    CborUlong CertificateIndex
) : Referenced<Cbor>;

[CborConverter(typeof(UnionConverter))]
public abstract record Option<T> : Cbor;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(0)]
public record Some<T>([CborProperty(0)] T Value) : Option<T>;

[CborConverter(typeof(ConstrConverter))]
[CborIndex(1)]
public record None<T> : Option<T>;

[CborConverter(typeof(MapConverter))]
public record MultiAssetOutput(Dictionary<CborBytes, TokenBundleOutput> Value) : Cbor;

[CborConverter(typeof(UnionConverter))]
public abstract record TokenBundle : Cbor;

[CborConverter(typeof(MapConverter))]
public record TokenBundleOutput(Dictionary<CborBytes, CborUlong> Value) : TokenBundle;

[CborConverter(typeof(MapConverter))]
public record TokenBundleMint(Dictionary<CborBytes, CborLong> Value) : TokenBundle;