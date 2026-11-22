using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Chrysalis.Wallet.Models.Enums;
using Xunit;

namespace Chrysalis.Tx.Test.Providers;

public class OuroborosTests
{
    private const string TxHash = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string ScriptHash = "33333333333333333333333333333333333333333333333333333333";
    private const string AddressBytes = "581d6000000000000000000000000000000000000000000000000000000000";
    private const string Key0 = "825820" + TxHash + "00";
    private const string Key1 = "825820" + TxHash + "01";
    private const string Key2 = "825820" + TxHash + "02";
    private const string ListOutput = "83" + AddressBytes + "1a001e848058202222222222222222222222222222222222222222222222222222222222222222";
    private const string MapOutput = "a300" + AddressBytes + "011a002dc6c003d818458200820180";
    private const string PlainOutput = "82" + AddressBytes + "1a000f4240";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ArgumentsThenCancellationThenUnsupported(bool metadata)
    {
        using CancellationTokenSource canceled = new(TimeSpan.FromSeconds(10));
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        await canceled.CancelAsync();
        Ouroboros provider = new("missing-test-node.sock");
        Task Invoke(string hash, CancellationToken ct) => metadata
            ? provider.GetTransactionMetadataAsync(hash, ct)
            : provider.GetUtxoByOutRefAsync(hash, 0, ct);
        await Assert.ThrowsAsync<ArgumentNullException>(() => Invoke(null!, canceled.Token));
        OperationCanceledException failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Invoke(BlockfrostTests.Hash, canceled.Token));
        Assert.Equal(canceled.Token, failure.CancellationToken);
        if (metadata)
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => Invoke(BlockfrostTests.Hash, timeout.Token));
        }
        else
        {
            Assert.True((await Assert.ThrowsAsync<ProviderException>(() => Invoke(BlockfrostTests.Hash, timeout.Token))).IsTransient);
        }
        failure = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetParametersAsync(canceled.Token));
        Assert.Equal(canceled.Token, failure.CancellationToken);
    }

    [Fact]
    public async Task CancellationClosesSocketAfterAcceptedHandshake()
    {
        using CancellationTokenSource caller = new(TimeSpan.FromSeconds(10));
        await NodeAsync(async (p, _) => await p.GetParametersAsync(caller.Token), async (stream, running, ct) =>
        {
            Assert.Equal(7, (await ReadSegmentAsync(stream, ct))?.Protocol);
            await caller.CancelAsync();
            OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
            Assert.Equal(caller.Token, error.CancellationToken);
            Assert.Null(error.InnerException);
            byte[] drain = new byte[8];
            while (await stream.ReadAsync(drain, ct) != 0)
            {
                // Any already queued protocol bytes can precede the close.
            }
        });
    }

    [Fact]
    public async Task SubmitsAConwayTransactionUnderTheConwayTagWithoutAskingTheTip()
    {
        ITransaction tx = ITransaction.Read(Fixture("transfer-ouroboros.cbor"), Era.Conway);
        string? id = null;
        List<(int Protocol, byte[] Payload)> sent = await ServeAsync(async (p, ct) => id = await p.SubmitTransactionAsync(tx, ct), "8101");
        Assert.All(sent, segment => Assert.Equal(6, segment.Protocol));
        Assert.Equal(Fixture("c12-out-p6-002.cbor"), Assert.Single(sent).Payload);
        using JsonDocument manifest = JsonDocument.Parse(Fixture("manifest.json"));
        string expectedId = manifest.RootElement.GetProperty("transactions").EnumerateArray()
            .Single(item => item.GetProperty("file").GetString() == "transactions/transfer-ouroboros.cbor")
            .GetProperty("txId").GetString()!;
        Assert.Equal(expectedId, id, StringComparer.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(Era.Babbage)]
    [InlineData(Era.Alonzo)]
    public async Task SubmitsUnderTheEraOfTheBody(Era era)
    {
        byte[] frame = Fixture("c10-out-p6-002.cbor");
        frame[3] = (byte)era;
        ITransaction tx = ITransaction.Read(frame.AsMemory(7), era);
        List<(int Protocol, byte[] Payload)> sent = await ServeAsync(async (p, ct) => _ = await p.SubmitTransactionAsync(tx, ct), "8101");
        (int protocol, byte[] payload) = Assert.Single(sent);
        Assert.Equal(6, protocol);
        Assert.Equal(frame, payload);
    }

    [Theory]
    [InlineData("c10-in-p6-002.cbor", ": transaction rejected", "Rejected in era 6: [[7, \"All inputs are spent. Transaction has probably already been included\"]]")]
    [InlineData("8103", ": unusable response: unexpected reply; the transaction may have been accepted", "")]
    [InlineData("8109", "; the transaction may have been accepted", "")]
    public async Task SubmitFailuresSayWhatTheNodeAnswered(string reply, string ending, string detail)
    {
        ArgumentNullException.ThrowIfNull(reply);
        string wire = reply.EndsWith(".cbor", StringComparison.Ordinal) ? Convert.ToHexString(Fixture(reply)) : reply;
        Exception? error = null;
        _ = await ServeAsync(async (p, ct) => error = await Record.ExceptionAsync(() => p.SubmitTransactionAsync(BlockfrostTests.Transaction, ct)), wire);
        ProviderException failure = Assert.IsType<ProviderException>(error);
        Assert.StartsWith("Ouroboros SubmitTransactionAsync: ", failure.Message, StringComparison.Ordinal);
        Assert.EndsWith(ending, failure.Message, StringComparison.Ordinal);
        Assert.Equal(detail, failure.Detail);
        Assert.False(failure.IsTransient);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task ALongRejectionReasonIsCappedInDetail()
    {
        string reply = "820281820679044C" + string.Concat(Enumerable.Repeat("6120", 550));
        Exception? error = null;
        _ = await ServeAsync(async (p, ct) => error = await Record.ExceptionAsync(() => p.SubmitTransactionAsync(BlockfrostTests.Transaction, ct)), reply);
        ProviderException failure = Assert.IsType<ProviderException>(error);
        Assert.Equal("Ouroboros SubmitTransactionAsync: transaction rejected", failure.Message);
        Assert.Equal(("Rejected in era 6: \"" + string.Concat(Enumerable.Repeat("a ", 550)) + "\"")[..1024], failure.Detail);
        Assert.False(failure.IsTransient);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public async Task AScriptFailureKeepsItsHashAndErrorInDetail()
    {
        // Shaped as cardano-ledger 1d78754a7 writes it (Evaluate.hs:408-418; Utxos.hs:290): the script in base64, its hash, the
        // evaluation error (plutus 52fc5c0a1 Cek/Internal.hs:623-624), the protocol version, a long context, then replay bytes.
        string script = Convert.ToBase64String([.. Enumerable.Range(0, 4096).Select(i => (byte)i)]);
        string address = "00" + new string('4', 112);
        string inputs = string.Concat(Enumerable.Range(0, 20).Select(i => $"    {TxHash}!{i} -> - Value (Map [(,Map [(\"\",3000000)])]) addressed to\n"
            + $"      ScriptCredential: {ScriptHash} (no staking credential)\n      with datum\n      NoOutputDatum\n"));
        byte[] text = Encoding.UTF8.GetBytes($"\nThe PlutusV3 script failed:\nBase64-encoded script bytes:\n\"{script}\"\nThe script hash is:ScriptHash \"{ScriptHash}\"\n"
            + "The plutus evaluation error is: CekError An error has occurred:\nThe machine terminated because of an error, either from a built-in function or from an explicit use of 'error'.\n"
            + $"The protocol version is: Version 10\nScriptInfo: SpendingScript {TxHash}!0\nTxInfo:\n  TxId: {TxHash}\n  Address: {address}\n  Inputs: [\n{inputs}  ]\n");
        byte[] replay = Encoding.ASCII.GetBytes(Convert.ToBase64String(new byte[6000]));
        // [2, [[6, [[1, [0, [0, [0, true, [1, [[1, text, replay]]]]]]]]]]: RejectTx; Conway Ledger.hs:247, Utxow.hs:238, Utxo.hs:327,
        // Utxos.hs:119 (IsValid true); Alonzo Utxos.hs:301 (FailedUnexpectedly) and :270 (PlutusFailure).
        byte[] reply = [.. Convert.FromHexString("8202818206818201820082008300F5820181830179"), .. Size(text), .. text, 0x59, .. Size(replay), .. replay];
        Exception? error = null;
        _ = await ServeAsync(async (p, ct) => error = await Record.ExceptionAsync(() => p.SubmitTransactionAsync(BlockfrostTests.Transaction, ct)), Convert.ToHexString(reply));
        string detail = Assert.IsType<ProviderException>(error).Detail;
        Assert.Contains($"\\\"{script[..8]}...(5464 characters)...{script[^8..]}\\\"", detail, StringComparison.Ordinal);
        Assert.Contains($"The script hash is:ScriptHash \\\"{ScriptHash}\\\"", detail, StringComparison.Ordinal);
        Assert.Contains("The plutus evaluation error is: CekError An error has occurred:", detail, StringComparison.Ordinal);
        Assert.Contains("The machine terminated because of an error, either from a built-in function or from an explicit use of 'error'.", detail, StringComparison.Ordinal);
        Assert.Contains($"TxId: {TxHash}", detail, StringComparison.Ordinal);
        Assert.Contains($"Address: {address}", detail, StringComparison.Ordinal);

        // A two-byte CBOR length; both items are under 65,536 bytes.
        static byte[] Size(byte[] item) => [(byte)(item.Length >> 8), (byte)item.Length];
    }

    [Fact]
    public async Task ReadsOutputsAsTheNodeEncodedThem()
    {
        List<ResolvedInput>? utxos = null;
        _ = await ServeAsync(
            async (p, ct) => utxos = await p.GetUtxosAsync([BlockfrostTests.Address], ct),
            "8101", "820406", "820481a3" + Key0 + ListOutput + Key1 + MapOutput + Key2 + PlainOutput);
        Assert.NotNull(utxos);
        Assert.Equal([0UL, 1UL, 2UL], utxos.Select(u => u.Outref.Index));
        Assert.Equal(
            [Convert.FromHexString(ListOutput), Convert.FromHexString(MapOutput), Convert.FromHexString(PlainOutput)],
            utxos.Select(u => CborSerializer.Serialize(u.Output)));
        Assert.Equal(Enumerable.Repeat((byte)0x22, 32), utxos[0].Output.DatumHash()!.Value.ToArray());
        AlonzoTransactionOutput plain = Assert.IsType<AlonzoTransactionOutput>(utxos[2].Output);
        Assert.Null(plain.DatumHash());
    }

    [Theory]
    [InlineData("820001")]
    [InlineData("a20000011a002dc6c0")]
    [InlineData("a300" + AddressBytes + "011a002dc6c00300")]
    [InlineData("83" + AddressBytes + "1a001e848000")]
    public async Task AnOutputWithAMalformedFieldIsUnusable(string output)
    {
        Exception? error = null;
        _ = await ServeAsync(
            async (p, ct) => error = await Record.ExceptionAsync(() => p.GetUtxosAsync([BlockfrostTests.Address], ct)),
            "8101", "820406", "820481a1" + Key0 + output);
        ProviderException failure = Assert.IsType<ProviderException>(error);
        Assert.StartsWith("Ouroboros GetUtxosAsync: unusable response: ", failure.Message, StringComparison.Ordinal);
        Assert.False(failure.IsTransient);
    }

    [Theory]
    [InlineData("a0", "null")]
    [InlineData("a1" + Key0 + ListOutput, "found")]
    [InlineData("a1" + Key1 + ListOutput, "wrong")]
    [InlineData("a2" + Key0 + ListOutput + Key1 + MapOutput, "wrong")]
    public async Task OutputReferenceLookupAsksTheNodeForThatInput(string answer, string expected)
    {
        ResolvedInput? found = null;
        Exception? error = null;
        List<(int Protocol, byte[] Payload)> sent = await ServeAsync(async (p, ct) =>
        {
            if (expected == "wrong")
            {
                error = await Record.ExceptionAsync(() => p.GetUtxoByOutRefAsync(TxHash, 0, ct));
            }
            else
            {
                found = await p.GetUtxoByOutRefAsync(TxHash, 0, ct);
            }
        }, "8101", "820406", "820481" + answer);
        Assert.Equal(
            CborSerializer.Serialize(QueryRequest.New(RawQueries.GetUtxoByTxIns([TransactionInput.Create(Convert.FromHexString(TxHash), 0)], Era.Conway))),
            sent[2].Payload);
        if (expected == "null")
        {
            Assert.Null(found);
        }
        else if (expected == "found")
        {
            Assert.NotNull(found);
            Assert.Equal(Convert.FromHexString(TxHash), found.Outref.TransactionId.ToArray());
            Assert.Equal(0UL, found.Outref.Index);
            Assert.Equal(Convert.FromHexString(ListOutput), CborSerializer.Serialize(found.Output));
        }
        else
        {
            ProviderException failure = Assert.IsType<ProviderException>(error);
            Assert.Equal("Ouroboros GetUtxoByOutRefAsync: unusable response: wrong output reference", failure.Message);
            Assert.False(failure.IsTransient);
        }
    }

    [Theory]
    [InlineData("InvalidOperationException", "The node's ledger is in era 0, which these helpers cannot query.", "820400")]
    [InlineData("InvalidOperationException", "Era mismatch: the query is for Conway (6), but the node's ledger is in Babbage (5).", "820406", "820482820666436F6E77617982056742616262616765")]
    [InlineData("CborDecodeException", "", "8109")]
    public async Task QueryRefusalsKeepTheHelpersText(string type, string text, params string[] replies)
    {
        Exception? error = null;
        _ = await ServeAsync(
            async (p, ct) => error = await Record.ExceptionAsync(() => p.GetParametersAsync(ct)),
            ["8101", .. replies]);
        ProviderException failure = Assert.IsType<ProviderException>(error);
        Assert.Equal($"Ouroboros GetParametersAsync: {type}", failure.Message);
        Assert.Equal(text, failure.Detail);
        Assert.False(failure.IsTransient);
    }

    [Theory]
    [InlineData(764824073UL, NetworkType.Mainnet)]
    [InlineData(1UL, NetworkType.Preprod)]
    [InlineData(2UL, NetworkType.Preview)]
    [InlineData(4UL, NetworkType.Unknown)]
    [InlineData(42UL, NetworkType.Unknown)]
    public void MagicNamesOnlyTheFixedNetworks(ulong magic, NetworkType network)
    {
        Assert.Equal(network, new Ouroboros("unused.sock", magic).NetworkType);
        if (network != NetworkType.Unknown)
        {
            Assert.Equal(magic, Ouroboros.GetNetworkMagic(network));
        }
    }

    [Theory]
    [InlineData(NetworkType.Testnet)]
    [InlineData(NetworkType.Unknown)]
    [InlineData((NetworkType)99)]
    public void OtherNetworksHaveNoFixedMagic(NetworkType network)
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => Ouroboros.GetNetworkMagic(network));
        Assert.Equal("networkType", error.ParamName);
    }

    [Fact]
    public async Task GuardsComeBeforeIo()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        Ouroboros provider = new("missing-test-node.sock");
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.SubmitTransactionAsync(null!, timeout.Token));
        ArgumentException bodyError = await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.SubmitTransactionAsync(ITransaction.Read(Convert.FromHexString("83a3008001800200a0f6"), Era.Allegra), timeout.Token));
        Assert.Equal("tx", bodyError.ParamName);
        ArgumentOutOfRangeException indexError = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            provider.GetUtxoByOutRefAsync(BlockfrostTests.Hash, 65536, timeout.Token));
        Assert.Equal("index", indexError.ParamName);
    }

    private static byte[] Fixture(string name) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "g1", name));

    // Starts call against a fake node on a fresh Unix socket, accepts its handshake (magic 2, the default), then runs node.
    private static async Task NodeAsync(Func<Ouroboros, CancellationToken, Task> call, Func<NetworkStream, Task, CancellationToken, Task> node)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        DirectoryInfo directory = Directory.CreateTempSubdirectory("t12");
        try
        {
            string path = Path.Combine(directory.FullName, "n.sock");
            using Socket listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(1);
            Task running = call(new Ouroboros(path), deadline.Token);
            Task<Socket> accepting = listener.AcceptAsync(deadline.Token).AsTask();
            Assert.True(await Task.WhenAny(running, accepting) == accepting, "The call ended without connecting.");
            using Socket socket = await accepting;
            using NetworkStream stream = new(socket);
            Assert.Equal(0, (await ReadSegmentAsync(stream, deadline.Token))?.Protocol);
            // Multiplexer responder bit, handshake protocol 0; [1, 32784, [2, false]].
            await stream.WriteAsync(Convert.FromHexString("000000008000000883011980108202F4"), deadline.Token);
            await node(stream, running, deadline.Token);
            Assert.False(deadline.IsCancellationRequested);
        }
        finally { directory.Delete(recursive: true); }
    }

    // Answers each request with the next reply on its protocol, then returns every segment the call sent after the handshake.
    private static async Task<List<(int Protocol, byte[] Payload)>> ServeAsync(Func<Ouroboros, CancellationToken, Task> call, params string[] replies)
    {
        List<(int Protocol, byte[] Payload)> sent = [];
        await NodeAsync(call, async (stream, running, ct) =>
        {
            foreach (string reply in replies)
            {
                (int protocol, byte[] payload) = await ReadSegmentAsync(stream, ct) ?? throw new InvalidOperationException("The call closed before its reply.");
                sent.Add((protocol, payload));
                byte[] body = Convert.FromHexString(reply);
                byte[] header = new byte[8];
                BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)(protocol | 0x8000));
                BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)body.Length);
                await stream.WriteAsync(header, ct);
                await stream.WriteAsync(body, ct);
            }
            await running;
            while (await ReadSegmentAsync(stream, ct) is { } rest) { sent.Add(rest); }
        });
        return sent;
    }

    private static async Task<(int Protocol, byte[] Payload)?> ReadSegmentAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[8];
        int read = await stream.ReadAtLeastAsync(header, 8, throwOnEndOfStream: false, cancellationToken);
        if (read == 0) { return null; }
        Assert.Equal(8, read);
        byte[] body = new byte[BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(6))];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return (BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(4)), body);
    }
}
