using System.Globalization;
using System.Reflection;
using Blake2Fast;
using Chrysalis.Codec.Extensions;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Header;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using SAIB.Cbor;

namespace Chrysalis.Codec.Test;

public class BorrowedCodecTests
{
    private const int PadPrefix = 3;
    private static readonly byte[] PadHead = [0xA5, 0x5A, 0xCC];
    private static readonly byte[] PadTail = [0xFE, 0xED];
    private static readonly byte[] AddrPrefix = Convert.FromHexString("581D61" + new string('0', 56));
    private static readonly int[] DefiniteLengths = [0, 1, 23, 24, 255, 256];
    private static readonly int[] ByronLengths = [0, 1, 125, 126, 127, 128, 129, 513];
    private static readonly string[] PrimitiveTags = ["", "D818", "D85F"];
    private static readonly byte[] ByronTags = [0, 1];
    private static readonly Type RomType = typeof(ReadOnlyMemory<byte>);
    private static readonly Type NullableRomType = typeof(ReadOnlyMemory<byte>?);
    private static readonly Type ByteArrayType = typeof(byte[]);

    private sealed class UnknownTxBody : ITransactionBody
    {
        public ReadOnlyMemory<byte> Raw => ReadOnlyMemory<byte>.Empty;
        public int ConstrIndex => -1;
        public bool IsIndefinite => false;
    }

    private static (byte[] Wire, int Offset, int Length) DefBytes(int length, byte firstByte = 0x11, string tagHex = "")
    {
        byte[] tag = tagHex.Length == 0 ? [] : Convert.FromHexString(tagHex);
        byte[] hdr = length < 24 ? [(byte)(0x40 + length)] : length <= 255 ? [0x58, (byte)length] : [0x59, (byte)(length >> 8), (byte)length];
        byte[] wire = new byte[tag.Length + hdr.Length + length];
        tag.CopyTo(wire, 0);
        hdr.CopyTo(wire, tag.Length);
        for (int i = 0; i < length; i++)
        {
            wire[tag.Length + hdr.Length + i] = i == 0 ? firstByte : (byte)(((i * 31) + 7) & 0xFF);
        }
        return (wire, tag.Length + hdr.Length, length);
    }

    private static (byte[] Buffer, ReadOnlyMemory<byte> Slice) Pad(byte[] wire)
    {
        byte[] buffer = [.. PadHead, .. wire, .. PadTail];
        return (buffer, buffer.AsMemory(PadPrefix, wire.Length + PadTail.Length));
    }

    private static byte[] PostAlonzoOutputWire(byte[]? datumCbor, bool hashOnly = false)
    {
        List<byte> bytes = [datumCbor is not null || hashOnly ? (byte)0xA3 : (byte)0xA2, 0x00, .. AddrPrefix, 0x01, 0x05];
        if (datumCbor is not null)
        {
            bytes.AddRange([0x02, 0x82, 0x01, .. datumCbor]);
        }
        else if (hashOnly)
        {
            bytes.AddRange([0x02, 0x82, 0x00, 0x58, 0x20, .. Enumerable.Repeat((byte)0xAB, 32)]);
        }
        return [.. bytes];
    }

    private static void AssertError(CborDecodeException error, int offset, string path, string expected, string actual)
    {
        Assert.Equal(offset, error.Offset);
        Assert.Equal(path, error.TypePath);
        Assert.Equal(expected, error.Expected);
        Assert.Equal(actual, error.Actual);
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"CBOR {path} at byte {offset}: expected {expected}; actual {actual}."), error.Message);
    }

    [Fact]
    public void PrimitiveMemoryPathsBorrowDefiniteSlicesAndTrackConsumed()
    {
        foreach (string tag in PrimitiveTags)
        {
            foreach (int len in DefiniteLengths)
            {
                (byte[] wire, int off, int n) = DefBytes(len, len == 1 ? (byte)0x5F : (byte)0x2A, tag);
                (_, ReadOnlyMemory<byte> input) = Pad(wire);
                ReadOnlyMemory<byte> expected = input.Slice(off, n);

                Assert.True(CborSerializer.Deserialize<ReadOnlyMemory<byte>>(input).Equals(expected));
                Assert.True(CborSerializer.Deserialize<ReadOnlyMemory<byte>>(input, out int c1).Equals(expected));
                Assert.True(CborSerializer.Deserialize<ReadOnlyMemory<byte>?>(input)!.Value.Equals(expected));
                Assert.True(CborSerializer.Deserialize<ReadOnlyMemory<byte>?>(input, out int c2)!.Value.Equals(expected));
                Assert.True(((ReadOnlyMemory<byte>)GenericSerializationUtil.Read(input, RomType)!).Equals(expected));
                Assert.True(((ReadOnlyMemory<byte>)GenericSerializationUtil.Read(input, NullableRomType)!).Equals(expected));
                Assert.True(((ReadOnlyMemory<byte>)GenericSerializationUtil.ReadAnyWithConsumed(input, RomType, out int c3)!).Equals(expected));
                Assert.True(((ReadOnlyMemory<byte>)GenericSerializationUtil.ReadAnyWithConsumed(input, NullableRomType, out int c4)!).Equals(expected));
                Assert.True(CborPrimitiveReaders.ReadByteMemory(input, out int c5).Equals(expected));
                Assert.Equal([wire.Length, wire.Length, wire.Length, wire.Length, wire.Length], [c1, c2, c3, c4, c5]);
                Assert.Equal(PadTail, input[c1..].ToArray());

                byte[] arrWire = [0x81, .. wire];
                (_, ReadOnlyMemory<byte> arrIn) = Pad(arrWire);
                CborList<ReadOnlyMemory<byte>> list = CborList<ReadOnlyMemory<byte>>.Read(arrIn, out int lc);
                Assert.Equal(arrWire.Length, lc);
                foreach (ReadOnlyMemory<byte> item in list)
                {
                    Assert.True(item.Equals(arrIn.Slice(1 + off, n)));
                }
                Assert.True(list.Single().Equals(arrIn.Slice(1 + off, n)));
            }
        }
    }

    [Fact]
    public void InlineDatumRawBorrowsDefiniteOpaqueBytesAndKeepsNullGuards()
    {
        foreach (int len in DefiniteLengths)
        {
            // 0xFF is invalid CBOR; inner inline datum bytes remain opaque.
            (byte[] datum, int off, int n) = DefBytes(len, len == 1 ? (byte)0x5F : (byte)0xFF, "D818");
            byte[] outWire = PostAlonzoOutputWire(datum);
            int datumStart = outWire.Length - datum.Length;
            (_, ReadOnlyMemory<byte> input) = Pad(outWire);
            PostAlonzoTransactionOutput output = PostAlonzoTransactionOutput.Read(input, out int consumed);
            Assert.Equal(outWire.Length, consumed);
            Assert.True(output.InlineDatumRaw()!.Value.Equals(input.Slice(datumStart + off, n)));
        }

        Assert.Null(PostAlonzoTransactionOutput.Read(PostAlonzoOutputWire(null, hashOnly: true)).InlineDatumRaw());
        Assert.Null(PostAlonzoTransactionOutput.Read(PostAlonzoOutputWire(null)).InlineDatumRaw());
        Assert.Null(AlonzoTransactionOutput.Read(Convert.FromHexString("8242010205")).InlineDatumRaw());
        _ = Assert.Throws<ArgumentNullException>(() => ((ITransactionOutput)null!).InlineDatumRaw());
    }

    [Fact]
    public void LazyCollectionMemoryBorrowsDefiniteItemsKeysAndValuesAndLooksUpByContent()
    {
        foreach (string tag in PrimitiveTags)
        {
            foreach (int len in DefiniteLengths)
            {
                (byte[] a, int aOff, int aLen) = DefBytes(len, len == 1 ? (byte)0x5F : (byte)0x33, tag);
                (byte[] b, int bOff, int bLen) = DefBytes(len == 0 ? 1 : len, 0x44, tag);

                byte[] listWire = [0x82, .. a, .. b];
                (_, ReadOnlyMemory<byte> listIn) = Pad(listWire);
                X32LazyBytesList lazyList = X32LazyBytesList.Read(listIn, out int lc);
                Assert.Equal(listWire.Length, lc);
                Assert.Equal(PadTail, listIn[lc..].ToArray());
                List<ReadOnlyMemory<byte>> items = lazyList.Value;
                Assert.True(items[0].Equals(listIn.Slice(1 + aOff, aLen)));
                Assert.True(items[1].Equals(listIn.Slice(1 + a.Length + bOff, bLen)));

                byte[] mapWire = [0xA1, .. a, .. b];
                (_, ReadOnlyMemory<byte> mapIn) = Pad(mapWire);
                X32LazyBytesMap lazyMap = X32LazyBytesMap.Read(mapIn, out int mc);
                Assert.Equal(mapWire.Length, mc);
                Assert.Equal(PadTail, mapIn[mc..].ToArray());
                Dictionary<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> map = lazyMap.Value;
                KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> pair = Assert.Single(map);
                Assert.True(pair.Key.Equals(mapIn.Slice(1 + aOff, aLen)));
                Assert.True(pair.Value.Equals(mapIn.Slice(1 + a.Length + bOff, bLen)));
                Assert.True(map.TryGetValue(pair.Key.ToArray(), out ReadOnlyMemory<byte> found));
                Assert.True(found.Equals(pair.Value));
            }
        }
    }

    [Theory]
    [InlineData("5FFF", "")]
    [InlineData("5F4040FF", "")]
    [InlineData("5F42AABBFF", "AABB")]
    [InlineData("5F4041AA42BBCCFF", "AABBCC")]
    [InlineData("D8185F4041AA42BBCCFF", "AABBCC")]
    public void IndefiniteByteStringsAndDetachedApisSurviveInputMutation(string wireHex, string expectedHex)
    {
        ArgumentNullException.ThrowIfNull(wireHex);
        byte[] wire = Convert.FromHexString(wireHex);
        byte[] expected = Convert.FromHexString(expectedHex);

        (byte[] pBuf, ReadOnlyMemory<byte> pIn) = Pad(wire);
        ReadOnlyMemory<byte> p1 = CborSerializer.Deserialize<ReadOnlyMemory<byte>>(pIn);
        ReadOnlyMemory<byte> p2 = CborSerializer.Deserialize<ReadOnlyMemory<byte>>(pIn, out int pc);
        ReadOnlyMemory<byte> p3 = (ReadOnlyMemory<byte>)GenericSerializationUtil.Read(pIn, RomType)!;
        ReadOnlyMemory<byte> p4 = (ReadOnlyMemory<byte>)GenericSerializationUtil.ReadAnyWithConsumed(pIn, NullableRomType, out int prc)!;
        ReadOnlyMemory<byte> p5 = CborPrimitiveReaders.ReadByteMemory(pIn, out int plc);
        byte[] arr = CborSerializer.Deserialize<byte[]>(pIn);
        Assert.Equal([wire.Length, wire.Length, wire.Length], [pc, prc, plc]);
        pBuf.AsSpan().Fill(0x99);
        ReadOnlyMemory<byte>[] reads = [p1, p2, p3, p4, p5, arr];
        foreach (ReadOnlyMemory<byte> got in reads)
        {
            Assert.Equal(expected, got.ToArray());
        }

        byte[] datumWire = wireHex.StartsWith("D818", StringComparison.Ordinal) ? wire : [0xD8, 0x18, .. wire];
        (byte[] oBuf, ReadOnlyMemory<byte> oIn) = Pad(PostAlonzoOutputWire(datumWire));
        PostAlonzoTransactionOutput post = PostAlonzoTransactionOutput.Read(oIn);
        ReadOnlyMemory<byte> inlineRaw = post.InlineDatumRaw()!.Value;
        byte[] detached = ((InlineDatumOption)post.Datum!).Data.GetValue();
        oBuf.AsSpan().Fill(0x99);
        Assert.Equal(expected, inlineRaw.ToArray());
        Assert.Equal(expected, detached);

        // Mixed definite + indefinite across list, map key, and map value; never mutate live borrowed map keys before lookup.
        (byte[] lBuf, ReadOnlyMemory<byte> lIn) = Pad([0x82, 0x42, 0x12, 0x34, .. wire]);
        List<ReadOnlyMemory<byte>> list = X32LazyBytesList.Read(lIn).Value;
        (byte[] mkBuf, ReadOnlyMemory<byte> mkIn) = Pad([0xA1, .. wire, 0x42, 0x56, 0x78]);
        Dictionary<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> keyMap = X32LazyBytesMap.Read(mkIn).Value;
        KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> keyPair = Assert.Single(keyMap);
        (byte[] mvBuf, ReadOnlyMemory<byte> mvIn) = Pad([0xA1, 0x42, 0x56, 0x78, .. wire]);
        KeyValuePair<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> valPair = Assert.Single(X32LazyBytesMap.Read(mvIn).Value);
        lBuf.AsSpan().Fill(0x99);
        mkBuf.AsSpan().Fill(0x77);
        mvBuf.AsSpan().Fill(0x66);
        Assert.Equal(expected, list[1].ToArray());
        Assert.Equal(expected, keyPair.Key.ToArray());
        Assert.Equal(expected, valPair.Value.ToArray());
        Assert.True(keyMap.ContainsKey(expected));
    }

    [Fact]
    public void DefiniteGetValueAndByteArrayRemainDetachedAfterMutation()
    {
        (byte[] bBuf, ReadOnlyMemory<byte> bIn) = Pad(Convert.FromHexString("43112233"));
        byte[] arr1 = CborSerializer.Deserialize<byte[]>(bIn);
        byte[] arr2 = (byte[])GenericSerializationUtil.Read(bIn, ByteArrayType)!;
        bBuf.AsSpan().Fill(0x99);
        Assert.Equal(Convert.FromHexString("112233"), arr1);
        Assert.Equal(Convert.FromHexString("112233"), arr2);

        (byte[] oBuf, ReadOnlyMemory<byte> oIn) = Pad(PostAlonzoOutputWire(Convert.FromHexString("D81843445566")));
        byte[] val = ((InlineDatumOption)PostAlonzoTransactionOutput.Read(oIn).Datum!).Data.GetValue();
        oBuf.AsSpan().Fill(0x99);
        Assert.Equal(Convert.FromHexString("445566"), val);
    }

    [Theory]
    [InlineData("5F5F41AAFFFF", 2, "byte string")]
    [InlineData("01", 1, "unsigned integer")]
    [InlineData("6161", 1, "text string")]
    [InlineData("D8186161", 3, "text string")]
    [InlineData("58", 1, "end of input")]
    [InlineData("5901", 1, "end of input")]
    [InlineData("D8", 1, "end of input")]
    [InlineData("DC", 1, "end of input")]
    [InlineData("42AA", 1, "end of input")]
    [InlineData("D81842AA", 3, "end of input")]
    [InlineData("5F42AAFF", 4, "end of input")]
    [InlineData("5F41AA", 3, "end of input")]
    [InlineData("D818D81841AA", 3, "semantic tag")]
    [InlineData("D818D8185F41AAFF", 3, "semantic tag")]
    public void MalformedByteStringsPinExactErrorsAcrossAllPaths(string hex, int offset, string actual)
    {
        byte[] wire = Convert.FromHexString(hex);
        string romPath = RomType.FullName!;
        AssertError(Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<ReadOnlyMemory<byte>>(wire)), offset, romPath, "byte string", actual);
        AssertError(Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<ReadOnlyMemory<byte>>(wire, out _)), offset, romPath, "byte string", actual);
        AssertError(Assert.Throws<CborDecodeException>(() => GenericSerializationUtil.Read(wire, RomType)), offset, romPath, "byte string", actual);
        AssertError(Assert.Throws<CborDecodeException>(() => GenericSerializationUtil.ReadAnyWithConsumed(wire, RomType, out _)), offset, romPath, "byte string", actual);

        bool validCbor = hex is "01" or "6161" or "D8186161" or "D818D81841AA" or "D818D8185F41AAFF";
        byte[] listWire = [0x81, .. wire];
        byte[] keyMapWire = [0xA1, .. wire, 0x41, 0xAA];
        byte[] valMapWire = [0xA1, 0x41, 0xAA, .. wire];
        byte[] outWire = PostAlonzoOutputWire(wire);
        if (validCbor)
        {
            X32LazyBytesList lazyList = X32LazyBytesList.Read(listWire);
            AssertError(Assert.Throws<CborDecodeException>(() => _ = lazyList.Value), 1 + offset, "Chrysalis.Codec.Test.X32LazyBytesList.Value.item", "byte string", actual);
            X32LazyBytesMap keyMap = X32LazyBytesMap.Read(keyMapWire);
            AssertError(Assert.Throws<CborDecodeException>(() => _ = keyMap.Value), 1 + offset, "Chrysalis.Codec.Test.X32LazyBytesMap.Value.entry", "byte string", actual);
            X32LazyBytesMap valMap = X32LazyBytesMap.Read(valMapWire);
            AssertError(Assert.Throws<CborDecodeException>(() => _ = valMap.Value), 3 + offset, "Chrysalis.Codec.Test.X32LazyBytesMap.Value.entry", "byte string", actual);
            PostAlonzoTransactionOutput post = PostAlonzoTransactionOutput.Read(outWire);
            AssertError(Assert.Throws<CborDecodeException>(() => _ = post.InlineDatumRaw()), 2 + offset, "Chrysalis.Codec.Types.Cardano.Core.Common.InlineDatumOption.Data.Chrysalis.Codec.Types.CborEncodedValue.Value", "byte string", actual);
        }
        else
        {
            _ = Assert.Throws<CborException>(() => X32LazyBytesList.Read(listWire));
            _ = Assert.Throws<CborException>(() => X32LazyBytesMap.Read(keyMapWire));
            _ = Assert.Throws<CborException>(() => X32LazyBytesMap.Read(valMapWire));
            _ = Assert.Throws<CborException>(() => PostAlonzoTransactionOutput.Read(outWire));
            _ = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<X32LazyBytesList>(listWire));
            _ = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<X32LazyBytesMap>(keyMapWire));
            _ = Assert.Throws<CborDecodeException>(() => CborSerializer.Deserialize<PostAlonzoTransactionOutput>(outWire));
        }
    }

    [Fact]
    public void TransactionBlockAndByronHashesMatchOneShotOracles()
    {
        IBlock[] blocks =
        [
            AlonzoCompatibleBlock.Read(Pad(Convert.FromHexString(File.ReadAllText(Path.Combine("TestData", "alonzo.block")).Trim())).Slice),
            BabbageBlock.Read(Pad(Convert.FromHexString(File.ReadAllText(Path.Combine("TestData", "babbage.block")).Trim())).Slice),
            ConwayBlock.Read(Pad(Convert.FromHexString(File.ReadAllText(Path.Combine("TestData", "conway.block")).Trim())).Slice),
        ];
        foreach (IBlock block in blocks)
        {
            BlockHeader header = block.Header();
            byte[] expectedHeaderBytes = Blake2b.HashData(32, header.Raw.Span);
            Assert.Equal(Convert.ToHexStringLower(expectedHeaderBytes), header.Hash());
            Assert.NotEqual(Convert.ToHexString(expectedHeaderBytes), header.Hash());

            ITransactionBody txBody = block.TransactionBodies().First();
            byte[] expectedTxBytes = Blake2b.HashData(32, txBody.Raw.Span);
            Assert.Equal(Convert.ToHexString(expectedTxBytes), txBody.Hash());
            Assert.NotEqual(Convert.ToHexStringLower(expectedTxBytes), txBody.Hash());
        }

        // Empty-Raw serializer fallback on a boxed decoded ConwayTransactionBody
        (_, ReadOnlyMemory<byte> paddedTx) = Pad(Convert.FromHexString("A3008001818242010205021864"));
        object boxed = ConwayTransactionBody.Read(paddedTx);
        typeof(ConwayTransactionBody).GetProperty("_raw", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(boxed, ReadOnlyMemory<byte>.Empty);
        ITransactionBody emptyRawBody = (ITransactionBody)boxed;
        Assert.True(emptyRawBody.Raw.IsEmpty);
        Assert.Equal(Convert.ToHexString(Blake2b.HashData(32, CborSerializer.Serialize(emptyRawBody))), emptyRawBody.Hash());

        Assert.Equal(Convert.ToHexStringLower(Blake2b.HashData(32, [])), default(BlockHeader).Hash());
        _ = Assert.Throws<ArgumentNullException>(() => ((ITransactionBody)null!).Hash());

        foreach (byte tag in ByronTags)
        {
            foreach (int len in ByronLengths)
            {
                byte[] payload = new byte[len];
                for (int i = 0; i < len; i++)
                {
                    payload[i] = (byte)(((i * 37) + tag + 11) & 0xFF);
                }
                (byte[] padded, _) = Pad(payload);
                ReadOnlySpan<byte> slice = padded.AsSpan(PadPrefix, len);
                byte[] legacy = [0x82, tag, .. slice];
                Assert.Equal(Blake2b.HashData(32, legacy), BlockExtensions.HashByronHeader(tag, slice));
            }
        }
    }

    [Fact]
    public void TransactionBodyOutputsMatchCastSequenceAcrossEras()
    {
        byte[] emptyBody = Convert.FromHexString("A3008001800200");
        byte[] twoOutBody = Convert.FromHexString("A300800182824201020182420304020205");

        Assert.Empty(AlonzoTransactionBody.Read(emptyBody).Outputs());
        Assert.Empty(BabbageTransactionBody.Read(emptyBody).Outputs());
        Assert.Empty(ConwayTransactionBody.Read(emptyBody).Outputs());

        AlonzoTransactionBody alonzoTwo = AlonzoTransactionBody.Read(twoOutBody);
        BabbageTransactionBody babbageTwo = BabbageTransactionBody.Read(twoOutBody);
        ConwayTransactionBody conwayTwo = ConwayTransactionBody.Read(twoOutBody);
        AssertOutputsEqual(alonzoTwo.Outputs(), alonzoTwo.Outputs.Cast<ITransactionOutput>());
        AssertOutputsEqual(babbageTwo.Outputs(), babbageTwo.Outputs.Cast<ITransactionOutput>());
        AssertOutputsEqual(conwayTwo.Outputs(), conwayTwo.Outputs.Cast<ITransactionOutput>());

        byte[] byronRaw = Convert.FromHexString(File.ReadAllText(Path.Combine("TestData", "byron2.block")).Trim());
        Assert.NotEmpty(BlockWithEra.Read(byronRaw).Block.TransactionBodies().First().Outputs());
        Assert.Empty(new UnknownTxBody().Outputs());
        _ = Assert.Throws<ArgumentNullException>(() => ((ITransactionBody)null!).Outputs());
    }

    private static void AssertOutputsEqual(IEnumerable<ITransactionOutput> actual, IEnumerable<ITransactionOutput> expected)
    {
        ITransactionOutput[] a = [.. actual];
        ITransactionOutput[] e = [.. expected];
        Assert.Equal(2, a.Length);
        Assert.Equal(e.Length, a.Length);
        for (int i = 0; i < e.Length; i++)
        {
            Assert.Equal(e[i].Address().ToArray(), a[i].Address().ToArray());
            Assert.Equal(e[i].Raw.ToArray(), a[i].Raw.ToArray());
        }
        Assert.NotEqual(a[0].Address().ToArray(), a[1].Address().ToArray());
    }
}
