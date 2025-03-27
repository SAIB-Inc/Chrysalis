using Chrysalis.Cbor.Types.Cardano.Core.Header;

namespace Chrysalis.Cbor.Extensions.Cardano.Core.Header;

public static class HeaderBodyExtensions
{
    public static ulong BlockNumber(this BlockHeaderBody self) =>
        self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.BlockNumber,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.BlockNumber,
            _ => throw new NotSupportedException()
        };

    public static ulong Slot(this BlockHeaderBody self) =>
        self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.Slot,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.Slot,
            _ => throw new NotSupportedException()
        };

    public static byte[] PrevHash(this BlockHeaderBody self) =>
        self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.PrevHash,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.PrevHash,
            _ => throw new NotSupportedException()
        };

    public static byte[] IssuerVKey(this BlockHeaderBody self) =>
        self switch
        {
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.IssuerVKey,
            _ => throw new NotSupportedException()
        };

    public static byte[] VrfKey(this BlockHeaderBody self) =>
        self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.VrfVKey,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.VrfVKey,
            _ => throw new NotSupportedException()
        };

    public static VrfCert VrfResult(this BlockHeaderBody self) =>
        self switch
        {
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.VrfResult,
            _ => throw new NotSupportedException()
        };

    public static ulong BlockBodySize(this BlockHeaderBody self) =>
        self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.BlockBodySize,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.BlockBodySize,
            _ => throw new NotSupportedException()
        };

    public static byte[] BlockBodyHash(this BlockHeaderBody self) =>
        self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.BlockBodyHash,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.BlockBodyHash,
            _ => throw new NotSupportedException()
        };

    public static ulong OperationalCertSequenceNumber(this BlockHeaderBody self) =>
        self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.OperationalCertSequenceNumber,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.OperationalCert.SequenceNumber(),
            _ => throw new NotSupportedException()
        };

    public static ulong OperationalCertKesPeriod(this BlockHeaderBody self) =>
        self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.OperationalCertKesPeriod,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.OperationalCert.KesPeriod(),
            _ => throw new NotSupportedException()
        };

    public static byte[] OperationalCertSigma(this BlockHeaderBody self) =>
        self switch
        {
            AlonzoHeaderBody alonzoHeaderBody => alonzoHeaderBody.OperationalCertSigma,
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.OperationalCert.Sigma(),
            _ => throw new NotSupportedException()
        };

    public static OperationalCert OperationalCert(this BlockHeaderBody self) =>
        self switch
        {
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.OperationalCert,
            _ => throw new NotSupportedException()
        };

    public static ProtocolVersion ProtocolVersion(this BlockHeaderBody self) =>
        self switch
        {
            BabbageHeaderBody babbageHeaderBody => babbageHeaderBody.ProtocolVersion,
            _ => throw new NotSupportedException()
        };
}