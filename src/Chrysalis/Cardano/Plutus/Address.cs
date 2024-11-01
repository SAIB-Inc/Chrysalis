using Chrysalis.Cardano.Cbor;
using Chrysalis.Cbor;

namespace Chrysalis.Cardano.Plutus;

[CborSerializable(CborType.Constr, Index = 0)]
public record Address(
    [CborProperty(0)]
    Credential PaymentCredential,

    [CborProperty(1)]
    Option<Inline<Credential>> StakeCredential
) : RawCbor;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(VerificationKey), typeof(Script)])]
public record Credential() : RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record VerificationKey([CborProperty(0)] CborBytes VerificationKeyHash) : Credential;

[CborSerializable(CborType.Constr, Index = 1)]
public record Script([CborProperty(0)] CborBytes ScriptHash) : Credential;

[CborSerializable(CborType.Union)]
[CborUnionTypes([typeof(Inline<>), typeof(Pointer)])]
public record Referenced<T>() : RawCbor;

[CborSerializable(CborType.Constr, Index = 0)]
public record Inline<T>(T Value) : Referenced<T>;

[CborSerializable(CborType.Constr, Index = 1)]
public record Pointer(
    [CborProperty(0)]
    CborUlong SlotNumber,

    [CborProperty(1)]
    CborUlong TransactionIndex,

    [CborProperty(2)]
    CborUlong CertificateIndex
) : Referenced<RawCbor>;