using Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

namespace Chrysalis.Cbor.Extensions.Block.Header;

public static class VrfCertExtensions
{
    public static byte[] Proof(this VrfCert self) => self.Proof;

    public static byte[] Output(this VrfCert self) => self.Output;
}