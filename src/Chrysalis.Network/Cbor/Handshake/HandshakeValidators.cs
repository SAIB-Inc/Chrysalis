using Chrysalis.Codec.Serialization;

namespace Chrysalis.Network.Cbor.Handshake;

// The existing declarative validator hook is required because these wire records
// overlap structurally. Index and family checks prevent first-match union mistakes.
internal sealed class N2CVersionValidator : ICborValidator<N2CVersion>
{
    public bool Validate(N2CVersion input) => input.Value is >= 32768 and <= 65535;
}

internal sealed class N2NVersionValidator : ICborValidator<N2NVersion>
{
    public bool Validate(N2NVersion input) => input.Value is >= 0 and < 32768;
}

internal sealed class ProposeVersionsValidator : ICborValidator<ProposeVersions>
{
    public bool Validate(ProposeVersions input) => input.Idx == 0;
}

internal sealed class N2NAcceptVersionValidator : ICborValidator<N2NAcceptVersion>
{
    public bool Validate(N2NAcceptVersion input) => input.Idx == 1;
}

internal sealed class N2CAcceptVersionValidator : ICborValidator<N2CAcceptVersion>
{
    public bool Validate(N2CAcceptVersion input) => input.Idx == 1;
}

internal sealed class RefuseValidator : ICborValidator<Refuse>
{
    public bool Validate(Refuse input) => input.Idx == 2;
}

internal sealed class N2NQueryReplyValidator : ICborValidator<N2NQueryReply>
{
    public bool Validate(N2NQueryReply input) => input.Idx == 3;
}

internal sealed class N2CQueryReplyValidator : ICborValidator<N2CQueryReply>
{
    public bool Validate(N2CQueryReply input) => input.Idx == 3;
}

internal sealed class VersionMismatchValidator : ICborValidator<VersionMismatch>
{
    public bool Validate(VersionMismatch input) => input.Idx == 0;
}

internal sealed class HandshakeDecodeErrorValidator : ICborValidator<HandshakeDecodeError>
{
    public bool Validate(HandshakeDecodeError input) => input.Idx == 1 && input.ErrorData is not null;
}

internal sealed class RefusedValidator : ICborValidator<Refused>
{
    public bool Validate(Refused input) => input.Idx == 2 && input.ErrorData is not null;
}
