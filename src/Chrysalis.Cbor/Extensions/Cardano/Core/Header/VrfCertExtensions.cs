using Chrysalis.Cbor.Types.Cardano.Core.Header;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Header;

public static class VrfCertExtensions
{
    public static byte[] Proof(this VrfCert self) => self.Proof;

    public static byte[] Output(this VrfCert self) => self.Output;
}