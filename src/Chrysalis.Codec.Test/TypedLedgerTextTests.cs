using System.Reflection;
using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Test;

public class TypedLedgerTextTests
{
    private const string Definite = "63616263";
    private const string Chunked = "7F6161626263FF";
    private static readonly string Hash32 = "5820" + new string('0', 64);
    private static readonly string Hash28 = "581C" + new string('0', 56);

    public enum Field { AnchorUrl, SingleHostDns, MultiHostDns, PoolMetadataUrl }

    public static TheoryData<Field, string> NonDefiniteFields()
    {
        TheoryData<Field, string> cases = [];
        foreach (Field field in Enum.GetValues<Field>())
        {
            foreach (string text in new[] { Chunked, "D820" + Definite, "F6", "7C", "7D", "7E", "7FFF" })
            {
                cases.Add(field, text);
            }
        }

        return cases;
    }

    [Theory]
    [InlineData(Field.AnchorUrl)]
    [InlineData(Field.SingleHostDns)]
    [InlineData(Field.MultiHostDns)]
    [InlineData(Field.PoolMetadataUrl)]
    public void StandaloneReaderAcceptsDefiniteText(Field field) => Assert.Equal("abc", GetText(ReadStandalone(field, Definite)));

    [Theory]
    [MemberData(nameof(NonDefiniteFields))]
    public void StandaloneReaderRequiresDefiniteText(Field field, string text)
    {
        CborDecodeException error = Assert.Throws<CborDecodeException>(() => ReadStandalone(field, text));
        AssertDefiniteTextError(field, error);
        Assert.Equal(field switch
        {
            Field.SingleHostDns => 3,
            Field.MultiHostDns => 2,
            Field.AnchorUrl or Field.PoolMetadataUrl => 1,
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        }, error.Offset);
    }

    [Theory]
    [InlineData(Field.AnchorUrl)]
    [InlineData(Field.SingleHostDns)]
    [InlineData(Field.MultiHostDns)]
    [InlineData(Field.PoolMetadataUrl)]
    public void TransactionReaderRejectsChunkedTextAtTheTypedLeaf(Field field)
    {
        ICertificate ReadCertificate(string text)
        {
            byte[] data = Convert.FromHexString($"84{Body(field, text)}A0F5F6");
            PostMaryTransaction tx = (PostMaryTransaction)ITransaction.Read(data, Era.Conway);
            return ((ConwayTransactionBody)tx.Body).Certificates!.GetValue().Single();
        }

        Assert.Equal("abc", GetText(ReadCertificateLeaf(ReadCertificate(Definite), field)));
        ICertificate certificate = ReadCertificate(Chunked);
        CborDecodeException error = Assert.Throws<CborDecodeException>(() => ReadCertificateLeaf(certificate, field));
        AssertDefiniteTextError(field, error);
    }

    [Theory]
    [InlineData(Field.AnchorUrl)]
    [InlineData(Field.SingleHostDns)]
    [InlineData(Field.MultiHostDns)]
    [InlineData(Field.PoolMetadataUrl)]
    public void BlockBodyReaderRejectsChunkedTextAtTheTypedLeaf(Field field)
    {
        ICertificate ReadCertificate(string text)
        {
            // The block header is an unused lazy slice; only the body is under test.
            byte[] data = Convert.FromHexString($"8207858081{Body(field, text)}81A0A080");
            ConwayBlock block = (ConwayBlock)BlockWithEra.Read(data).Block;
            return block.TransactionBodies.GetValue().Single().Certificates!.GetValue().Single();
        }

        Assert.Equal("abc", GetText(ReadCertificateLeaf(ReadCertificate(Definite), field)));
        ICertificate certificate = ReadCertificate(Chunked);
        CborDecodeException error = Assert.Throws<CborDecodeException>(() => ReadCertificateLeaf(certificate, field));
        AssertDefiniteTextError(field, error);
    }

    private static void AssertDefiniteTextError(Field field, CborDecodeException error)
    {
        string fieldPath = field switch
        {
            Field.AnchorUrl => ".Anchor.Url",
            Field.SingleHostDns => ".SingleHostName.DnsName",
            Field.MultiHostDns => ".MultiHostName.DnsName",
            Field.PoolMetadataUrl => ".PoolMetadata.Url",
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        Assert.Equal("definite-length text", error.Expected);
        Assert.EndsWith(fieldPath, error.TypePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(11, Definite, true)]
    [InlineData(11, Chunked, false)]
    [InlineData(12, Definite, true)]
    [InlineData(12, Chunked, true)]
    public void InternalTextScanUsesTheDecoderProtocolVersion(int version, string text, bool accepts)
    {
        // Test the internal version seam without adding a public API or friend assembly.
        TextSkip skip = typeof(CborSerializer).Assembly.GetType("Chrysalis.Codec.Serialization.LedgerText", throwOnError: true)!
            .GetMethod("Skip", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<TextSkip>();
        byte[] data = Convert.FromHexString(text + "00");
        int Scan()
        {
            CborReader reader = new(data);
            skip(ref reader, data, "typed text", version);
            return reader.Buffer.Length;
        }

        if (accepts)
        {
            Assert.Equal(1, Scan());
        }
        else
        {
            CborDecodeException error = Assert.Throws<CborDecodeException>(() => Scan());
            Assert.Equal("definite-length text", error.Expected);
        }
    }

    private delegate void TextSkip(ref CborReader reader, ReadOnlyMemory<byte> data, string typePath, int protocolVersion);

    private static ICborType ReadStandalone(Field field, string text) => field switch
    {
        Field.AnchorUrl => Anchor.Read(Convert.FromHexString($"82{text}{Hash32}")),
        Field.SingleHostDns => SingleHostName.Read(Convert.FromHexString(Relay(field, text))),
        Field.MultiHostDns => MultiHostName.Read(Convert.FromHexString(Relay(field, text))),
        Field.PoolMetadataUrl => PoolMetadata.Read(Convert.FromHexString($"82{text}{Hash32}")),
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static string Body(Field field, string text)
    {
        // One certificate at body key 4; the other keys are inputs, outputs and fee.
        const string prefix = "A40080018002000481";
        if (field == Field.AnchorUrl)
        {
            return $"{prefix}830F8200{Hash28}82{text}{Hash32}";
        }

        // Pool certificate: tag, operator, VRF hash, pledge, cost, margin, reward account, owners.
        string pool = $"{prefix}8A03{Hash28}{Hash32}0000D81E820001581DE0{new string('0', 56)}80";
        return field == Field.PoolMetadataUrl ? $"{pool}8082{text}{Hash32}" : $"{pool}81{Relay(field, text)}F6";
    }

    private static string Relay(Field field, string text) => field == Field.SingleHostDns ? $"8301F6{text}" : $"8202{text}";

    private static ICborType ReadCertificateLeaf(ICertificate certificate, Field field) => field switch
    {
        Field.AnchorUrl => ((ResignCommitteeColdCert)certificate).Anchor!.Value,
        Field.SingleHostDns or Field.MultiHostDns => ((PoolRegistration)certificate).Relays.GetValue().Single(),
        Field.PoolMetadataUrl => ((PoolRegistration)certificate).PoolMetadata!.Value,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static string GetText(ICborType value) => value switch
    {
        Anchor anchor => anchor.Url,
        SingleHostName single => single.DnsName,
        MultiHostName multi => multi.DnsName,
        PoolMetadata metadata => metadata.Url,
        _ => throw new InvalidOperationException()
    };
}
