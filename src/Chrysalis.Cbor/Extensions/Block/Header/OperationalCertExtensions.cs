using Chrysalis.Cbor.Cardano.Types.Block.Header.Body;

namespace Chrysalis.Cbor.Extensions.Block.Header;

public static class OperationalCertExtensions
{
    public static byte[] HotVKey(this OperationalCert self) => self.HotVKey;

    public static ulong SequenceNumber(this OperationalCert self) => self.SequenceNumber;

    public static ulong KesPeriod(this OperationalCert self) => self.KesPeriod;

    public static byte[] Sigma(this OperationalCert self) => self.Sigma;
}