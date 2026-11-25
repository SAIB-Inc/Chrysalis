using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Cbor.LocalStateQuery.Messages;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Tests;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Chrysalis.Tx.Test.Cli;
using Chrysalis.Tx.Utils;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Utils;
using Xunit;

namespace Chrysalis.Tx.Test.Providers;

public class OuroborosTests
{
    private const string TxHash = "1111111111111111111111111111111111111111111111111111111111111111";
    private const string OtherTxHash = "2222222222222222222222222222222222222222222222222222222222222222";
    private const string ScriptHash = "33333333333333333333333333333333333333333333333333333333";
    private const string AddressBytes = "581d6000000000000000000000000000000000000000000000000000000000";
    private const string Key0 = "825820" + TxHash + "00";
    private const string Key1 = "825820" + TxHash + "01";
    private const string Key2 = "825820" + TxHash + "02";
    private const string KeyOtherTx0 = "825820" + OtherTxHash + "00";
    // Multiplexer responder bit, handshake protocol 0; [1, 32784, [2, false]].
    private static readonly byte[] HandshakeAccept = Convert.FromHexString("000000008000000883011980108202F4");
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
        await using Ouroboros provider = new("missing-test-node.sock");
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
            await DrainToEofAsync(stream, ct);
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
    [InlineData("a1" + KeyOtherTx0 + ListOutput, "wrong")]
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
        using Ouroboros provider = new("unused.sock", magic);
        Assert.Equal(network, provider.NetworkType);
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
        await using Ouroboros provider = new("missing-test-node.sock");
        await Assert.ThrowsAsync<ArgumentNullException>(() => provider.SubmitTransactionAsync(null!, timeout.Token));
        ArgumentException bodyError = await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.SubmitTransactionAsync(ITransaction.Read(Convert.FromHexString("83a3008001800200a0f6"), Era.Allegra), timeout.Token));
        Assert.Equal("tx", bodyError.ParamName);
        ArgumentOutOfRangeException indexError = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            provider.GetUtxoByOutRefAsync(BlockfrostTests.Hash, 65536, timeout.Token));
        Assert.Equal("index", indexError.ParamName);
        ArgumentException addressError = await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.GetUtxosAsync([Bech32Util.Encode([0x60, 0x01, 0x02], "addr_test")], timeout.Token));
        Assert.Equal("address", addressError.ParamName);
    }

    [Fact]
    public async Task ShortenPins128And129CharacterBoundary()
    {
        string kept = new('A', 128), shortened = new('B', 129);
        byte[] text = Encoding.ASCII.GetBytes($"{kept} {shortened}");
        byte[] reply = [.. Convert.FromHexString("820281820679"), (byte)(text.Length >> 8), (byte)text.Length, .. text];
        Exception? error = null;
        _ = await ServeAsync(async (p, ct) => error = await Record.ExceptionAsync(() => p.SubmitTransactionAsync(BlockfrostTests.Transaction, ct)), Convert.ToHexString(reply));
        Assert.Equal($"Rejected in era 6: \"{kept} BBBBBBBB...(129 characters)...BBBBBBBB\"", Assert.IsType<ProviderException>(error).Detail);
    }

    [Fact]
    public async Task LongQueryRefusalIsCappedInDetail()
    {
        string eraName = new('x', 1100);
        string reply = "820482820666436F6E776179820579044C" + string.Concat(Enumerable.Repeat("78", 1100));
        Exception? error = null;
        _ = await ServeAsync(async (p, ct) => error = await Record.ExceptionAsync(() => p.GetParametersAsync(ct)), "8101", "820406", reply);
        string detail = Assert.IsType<ProviderException>(error).Detail;
        Assert.Equal(1024, detail.Length);
        Assert.Equal($"Era mismatch: the query is for Conway (6), but the node's ledger is in {eraName} (5)."[..1024], detail);
    }

    [Fact]
    public async Task SequentialCallsReuseOneSocket()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        string paramsWire = Convert.ToHexString(await File.ReadAllBytesAsync(Path.Combine(CliVectors.Root, CliVectors.Wire), deadline.Token));
        ConcurrentQueue<string> replies = new([
            "8101", "820406", paramsWire,
            "8101", "820406", "820481a1" + Key0 + ListOutput,
            "8101", "820406", "820481a1" + Key0 + ListOutput,
            "8101"
        ]);
        await using FakeNode node = FakeNode.Start((stream, ct) => ServeRepliesAsync(stream, replies, ct));
        await using Ouroboros provider = new(node.SocketPath);
        Assert.NotNull(await provider.GetParametersAsync(deadline.Token));
        Assert.Single(await provider.GetUtxosAsync([BlockfrostTests.Address], deadline.Token));
        ResolvedInput? outref = await provider.GetUtxoByOutRefAsync(TxHash, 0, deadline.Token);
        Assert.NotNull(outref);
        Assert.Equal(0UL, outref.Outref.Index);
        Assert.Equal(((PostMaryTransaction)BlockfrostTests.Transaction).Body.Hash(),
            await provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token), StringComparer.OrdinalIgnoreCase);
        Assert.Empty(replies);
        Assert.Equal(1, node.Accepts);
    }

    // cardano-cli's preview era history: every era start's slot and time is the node's own pair.
    [Fact]
    public async Task SlotProfileIsTheNodesSystemStartAndEraHistory()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using JsonDocument file = JsonDocument.Parse(await File.ReadAllBytesAsync(Path.Combine(CliVectors.Root, "inputs", "cardano-cli", "preview-era-history.json"), deadline.Token));
        string history = file.RootElement.GetProperty("cborHex").GetString() ?? "";
        ConcurrentQueue<string> replies = new(["8101", "8204831907e619012a00", "8101", "8204" + history]);
        await using FakeNode node = FakeNode.Start((stream, ct) => ServeRepliesAsync(stream, replies, ct));
        await using Ouroboros provider = new(node.SocketPath);
        SlotNetworkConfig profile = await provider.GetSlotNetworkConfigAsync(deadline.Token);
        Assert.Equal(SlotNetworkConfig.Preview.SystemStart, profile.SystemStart);
        Assert.Equal(74_476_800UL, profile.EndSlot);
        foreach (EraSummary era in CborSerializer.Deserialize<EraHistoryResponse>(Convert.FromHexString(history)).Summaries)
        {
            Assert.Equal(1_666_656_000 + (long)(era.Start.TimePicoseconds / 1_000_000_000_000), SlotUtil.GetPosixTimeSecondsFromSlot(profile, (long)era.Start.Slot));
        }
        Assert.Equal(SlotUtil.GetUTCTimeFromSlot(SlotNetworkConfig.Preview, 74_476_799), SlotUtil.GetUTCTimeFromSlot(profile, 74_476_799));
        Assert.Throws<SlotConversionException>(() => SlotUtil.GetUTCTimeFromSlot(profile, 74_476_800));
        Assert.Empty(replies);
        Assert.Equal(1, node.Accepts);
    }

    [Fact]
    public async Task AnEmptyEraHistoryIsUnusable()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        ConcurrentQueue<string> replies = new(["8101", "8204831907e619012a00", "8101", "820480"]);
        await using FakeNode node = FakeNode.Start((stream, ct) => ServeRepliesAsync(stream, replies, ct));
        await using Ouroboros provider = new(node.SocketPath);
        ProviderException error = await Assert.ThrowsAsync<ProviderException>(() => provider.GetSlotNetworkConfigAsync(deadline.Token));
        Assert.False(error.IsTransient);
        Assert.Contains("unusable response", error.Message, StringComparison.Ordinal);
    }

    // T12b MINOR 1: a call cancelled before its handshake closes its socket at once, not when the provider is disposed.
    [Fact]
    public async Task CancelBeforeTheHandshakeClosesTheSocketWhileTheProviderLives()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using CancellationTokenSource caller = new();
        await using FakeNode node = FakeNode.Start((_, _) => Task.CompletedTask);
        await using Ouroboros provider = new(node.SocketPath) { OnClientConnected = _ => CancelSync(caller) };
        OperationCanceledException error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetParametersAsync(caller.Token));
        Assert.Equal(caller.Token, error.CancellationToken);
        await node.ClosedBeforeHandshake.WaitAsync(deadline.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CleanPeerCloseMidCallIsTransientAndSubmitWarnsMayHaveBeenAccepted(bool submit)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        await using FakeNode node = FakeNode.Start(async (stream, ct) => _ = await ReadSegmentAsync(stream, ct));
        await using Ouroboros provider = new(node.SocketPath);
        ProviderException failure = await Assert.ThrowsAsync<ProviderException>(() => submit
            ? provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token)
            : provider.GetParametersAsync(deadline.Token));
        AssertConnectionClosed(failure, submit);
    }

    [Theory]
    [InlineData("reject")]
    [InlineData("query-refusal")]
    [InlineData("peer-close")]
    [InlineData("cancel-mid-call")]
    public async Task FailedOrCancelledCallClosesConnectionAndNextCallOpensFreshSocket(string kind)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        TaskCompletionSource socket1Closed = Gate(), requestReceived = Gate();
        await using FakeNode node = FakeNode.Start(async (accept, stream, ct) =>
        {
            if (accept == 2) { await AcceptSubmitToEofAsync(stream, ct); return; }
            _ = await ReadSegmentAsync(stream, ct);
            if (kind == "reject") { await SendReplyAsync(stream, 6, Convert.ToHexString(Fixture("c10-in-p6-002.cbor")), ct); }
            else if (kind == "query-refusal")
            {
                await SendReplyAsync(stream, 7, "8101", ct);
                _ = await ReadSegmentAsync(stream, ct);
                await SendReplyAsync(stream, 7, "820400", ct);
            }
            else if (kind == "cancel-mid-call") { requestReceived.TrySetResult(); }
            if (kind != "peer-close") { await DrainToEofAsync(stream, ct); }
            socket1Closed.TrySetResult();
        });
        await using Ouroboros provider = new(node.SocketPath);
        if (kind == "cancel-mid-call")
        {
            using CancellationTokenSource caller = new(TimeSpan.FromSeconds(10));
            Task pending = provider.GetParametersAsync(caller.Token);
            await requestReceived.Task.WaitAsync(deadline.Token);
            await caller.CancelAsync();
            Assert.Equal(caller.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending)).CancellationToken);
        }
        else
        {
            _ = await Assert.ThrowsAsync<ProviderException>(() => kind == "reject"
                ? provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token)
                : provider.GetParametersAsync(deadline.Token));
        }
        await socket1Closed.Task.WaitAsync(deadline.Token);
        Assert.NotEmpty(await provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token));
        Assert.Equal(2, node.Accepts);
    }

    [Fact]
    public async Task IdleConnectionEndedByPeerIsReplacedSilently()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        TaskCompletionSource closeSocket1 = Gate();
        await using FakeNode node = FakeNode.Start(async (accept, stream, ct) =>
        {
            if (accept == 2) { await AcceptSubmitToEofAsync(stream, ct); return; }
            await ReadConwayQueryAsync(stream, ct);
            await SendReplyAsync(stream, 7, "820481a1" + Key0 + ListOutput, ct);
            _ = await ReadSegmentAsync(stream, ct); // Release(5)
            await closeSocket1.Task.WaitAsync(ct);
        });
        await using Ouroboros provider = new(node.SocketPath);
        NodeClient? firstClient = null;
        provider.OnClientConnected = client => firstClient ??= client;
        Assert.Single(await provider.GetUtxosAsync([BlockfrostTests.Address], deadline.Token));
        Assert.Equal(1, node.Accepts);
        Assert.NotNull(firstClient);
        closeSocket1.TrySetResult();
        _ = await Assert.ThrowsAsync<ConnectionClosedException>(() => firstClient.Completion.WaitAsync(deadline.Token));
        Assert.NotEmpty(await provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token));
        Assert.Equal(2, node.Accepts);
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => firstClient.StartAsync(2, deadline.Token));
    }

    [Fact]
    public async Task OverlappingCallsOpenSeparateSocketsAndKeepAtMostOneIdle()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        TaskCompletionSource call1Entered = Gate(), releaseCall1 = Gate(), oneSocketClosed = Gate();
        ConcurrentQueue<string> followUpReplies = new(["8101"]);
        await using FakeNode node = FakeNode.Start(async (accept, stream, ct) =>
        {
            if (accept == 1)
            {
                await ReadConwayQueryAsync(stream, ct);
                call1Entered.TrySetResult();
                await releaseCall1.Task.WaitAsync(ct);
                await SendReplyAsync(stream, 7, "820481a1" + Key0 + ListOutput, ct);
            }
            else
            {
                _ = await ReadSegmentAsync(stream, ct);
                await SendReplyAsync(stream, 6, "8101", ct);
            }
            await ServeRepliesAsync(stream, followUpReplies, ct);
            oneSocketClosed.TrySetResult();
        });
        await using Ouroboros provider = new(node.SocketPath);
        Task<List<ResolvedInput>> call1 = provider.GetUtxosAsync([BlockfrostTests.Address], deadline.Token);
        await call1Entered.Task.WaitAsync(deadline.Token);
        Assert.NotEmpty(await provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token));
        Assert.Equal(2, node.Accepts);
        releaseCall1.TrySetResult();
        Assert.Single(await call1);
        await oneSocketClosed.Task.WaitAsync(deadline.Token);
        Assert.NotEmpty(await provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token));
        Assert.Equal(2, node.Accepts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposeAndDisposeAsyncCloseIdleSocketAndAreIdempotent(bool useAsync)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        TaskCompletionSource socketClosed = Gate(), releaseServer = Gate();
        TaskCompletionSource<Socket> accepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using FakeNode node = FakeNode.Start(async (stream, ct) =>
        {
            _ = await ReadSegmentAsync(stream, ct);
            await SendReplyAsync(stream, 6, "8101", ct);
            accepted.TrySetResult(stream.Socket);
            if (useAsync) { await releaseServer.Task.WaitAsync(ct); return; }
            await DrainToEofAsync(stream, ct);
            socketClosed.TrySetResult();
        });
        await using Ouroboros provider = new(node.SocketPath);
        _ = await provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token);
        Assert.Equal(1, node.Accepts);
        Socket socket = await accepted.Task.WaitAsync(deadline.Token);
        if (useAsync) { await provider.DisposeAsync(); AssertClosedNow(socket); releaseServer.TrySetResult(); }
        else { DisposeSync(provider); await socketClosed.Task.WaitAsync(deadline.Token); }
        DisposeSync(provider);
        await provider.DisposeAsync();
    }

    [Theory]
    [InlineData("query-sync")]
    [InlineData("query-async")]
    [InlineData("submit-async")]
    [InlineData("handshake-async")]
    [InlineData("cancel-with-dispose")]
    public async Task DisposeDuringInFlightCallWakesCallerWithObjectDisposedException(string mode)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        bool isHandshake = mode == "handshake-async";
        bool holdSocket = mode is "query-async" or "submit-async" or "handshake-async";
        TaskCompletionSource entered = Gate(), socketClosed = Gate(), releaseServer = Gate();
        TaskCompletionSource<Socket> accepted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using FakeNode node = FakeNode.Start(async (stream, ct) =>
        {
            if (!isHandshake) { _ = await ReadSegmentAsync(stream, ct); }
            accepted.TrySetResult(stream.Socket);
            entered.TrySetResult();
            if (holdSocket) { await releaseServer.Task.WaitAsync(ct); return; }
            await DrainToEofAsync(stream, ct);
            socketClosed.TrySetResult();
        }, acceptHandshake: !isHandshake);
        await using Ouroboros provider = new(node.SocketPath);
        if (mode == "cancel-with-dispose")
        {
            using CancellationTokenSource caller = new(TimeSpan.FromSeconds(10));
            Task cancelledCall = provider.GetParametersAsync(caller.Token);
            await entered.Task.WaitAsync(deadline.Token);
            await using CancellationTokenRegistration registration = caller.Token.Register(() => DisposeSync(provider));
            await caller.CancelAsync();
            OperationCanceledException cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledCall);
            Assert.Equal(caller.Token, cancelled.CancellationToken);
            Assert.Null(cancelled.InnerException);
            await socketClosed.Task.WaitAsync(deadline.Token);
            return;
        }
        Task pending = mode == "submit-async"
            ? provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token)
            : provider.GetParametersAsync(deadline.Token);
        await entered.Task.WaitAsync(deadline.Token);
        Socket socket = await accepted.Task.WaitAsync(deadline.Token);
        if (mode == "query-sync") { DisposeSync(provider); }
        else { await provider.DisposeAsync(); AssertClosedNow(socket); releaseServer.TrySetResult(); }
        ObjectDisposedException error = await Assert.ThrowsAsync<ObjectDisposedException>(() => pending);
        Assert.Contains(nameof(Ouroboros), error.ObjectName, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(NodeClient), error.ObjectName, StringComparison.Ordinal);
        if (!holdSocket) { await socketClosed.Task.WaitAsync(deadline.Token); }
    }

    [Fact]
    public async Task DisposeAfterReplyAlreadyDecodedReturnsResultAndClosesSocket()
    {
        TaskCompletionSource socketClosed = Gate();
        await SubmitOnReplyDisposeAsync(
            async (stream, ct) => { await DrainToEofAsync(stream, ct); socketClosed.TrySetResult(); },
            deadline => deadline.Token,
            DisposeSync,
            async (provider, _, ct) =>
            {
                await socketClosed.Task.WaitAsync(ct);
                await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.GetParametersAsync(ct));
            });
    }

    [Fact]
    public async Task CallsAfterDisposeThrowObjectDisposedExceptionWithoutIo()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        using CancellationTokenSource canceled = new(TimeSpan.FromSeconds(10));
        await canceled.CancelAsync();
        await using FakeNode node = FakeNode.Start((_, _) => Task.CompletedTask);
        await using Ouroboros provider = new(node.SocketPath);
        await provider.DisposeAsync();
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.GetParametersAsync(deadline.Token));
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.GetUtxosAsync([BlockfrostTests.Address], deadline.Token));
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.GetUtxosAsync([], deadline.Token));
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.GetUtxoByOutRefAsync(TxHash, 0, deadline.Token));
        _ = await Assert.ThrowsAsync<ObjectDisposedException>(() => provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token));
        Assert.Equal(0, node.Accepts);
        _ = await Assert.ThrowsAsync<NotSupportedException>(() => provider.GetTransactionMetadataAsync(TxHash, deadline.Token));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => provider.GetUtxosAsync(null!, deadline.Token));
        _ = await Assert.ThrowsAsync<ArgumentNullException>(() => provider.SubmitTransactionAsync(null!, deadline.Token));
        _ = await Assert.ThrowsAsync<ArgumentException>(() => provider.GetUtxoByOutRefAsync("bad", 0, deadline.Token));
        Assert.Equal(canceled.Token, (await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetParametersAsync(canceled.Token))).CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DefaultReplyTimeLimitIs180SecondsAndCutsSilentQueryAndSubmit(bool submit)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        ManualClock clock = new();
        TaskCompletionSource requestReceived = Gate(), socket1Closed = Gate();
        await using FakeNode node = FakeNode.Start(async (accept, stream, ct) =>
        {
            if (accept == 2) { await AcceptSubmitToEofAsync(stream, ct); return; }
            _ = await ReadSegmentAsync(stream, ct);
            requestReceived.TrySetResult();
            await DrainToEofAsync(stream, ct);
            socket1Closed.TrySetResult();
        });
        await using Ouroboros provider = new(node.SocketPath) { OnClientConnected = client => AttachClock(client, clock) };
        Assert.Equal(TimeSpan.FromSeconds(180), provider.ReplyTimeLimit);
        Task pending = submit
            ? provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token)
            : provider.GetParametersAsync(deadline.Token);
        await requestReceived.Task.WaitAsync(deadline.Token);
        Assert.Equal(TimeSpan.FromSeconds(180), clock.Timers[^1].Due);
        Advance(clock, TimeSpan.FromSeconds(180));
        ProviderException failure = await Assert.ThrowsAsync<ProviderException>(() => pending);
        AssertConnectionClosed(failure, submit);
        await socket1Closed.Task.WaitAsync(deadline.Token);
        _ = await provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token);
        Assert.Equal(2, node.Accepts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueryLongerThan180SecondsSucceedsWithRaisedOrInfiniteReplyTimeLimit(bool infinite)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        ManualClock clock = new();
        TaskCompletionSource query1Received = Gate(), releaseQuery1 = Gate(), query2Received = Gate();
        await using FakeNode node = FakeNode.Start(async (stream, ct) =>
        {
            await ReadConwayQueryAsync(stream, ct);
            query1Received.TrySetResult();
            await releaseQuery1.Task.WaitAsync(ct);
            await SendReplyAsync(stream, 7, "820481a1" + Key0 + ListOutput, ct);
            _ = await ReadSegmentAsync(stream, ct); // Release(5)
            if (infinite) { await AcceptSubmitToEofAsync(stream, ct); return; }
            await ReadConwayQueryAsync(stream, ct);
            query2Received.TrySetResult();
            await DrainToEofAsync(stream, ct);
        });
        await using Ouroboros provider = new(node.SocketPath)
        {
            ReplyTimeLimit = infinite ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(300),
            OnClientConnected = client => AttachClock(client, clock)
        };
        Task<List<ResolvedInput>> call1 = provider.GetUtxosAsync([BlockfrostTests.Address], deadline.Token);
        await query1Received.Task.WaitAsync(deadline.Token);
        if (infinite)
        {
            ManualTimer handshakeTimer = Assert.Single(clock.Timers);
            Assert.Equal(TimeSpan.FromSeconds(10), handshakeTimer.Due);
            Assert.True(handshakeTimer.Disposed);
            Assert.False(call1.IsCompleted);
            releaseQuery1.TrySetResult();
            Assert.Single(await call1);
            _ = await provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token);
            Assert.Equal(1, node.Accepts);
            return;
        }
        Assert.Equal(TimeSpan.FromSeconds(300), clock.Timers[^1].Due);
        Advance(clock, TimeSpan.FromSeconds(200));
        Assert.False(call1.IsCompleted);
        releaseQuery1.TrySetResult();
        Assert.Single(await call1);
        Task<List<ResolvedInput>> call2 = provider.GetUtxosAsync([BlockfrostTests.Address], deadline.Token);
        await query2Received.Task.WaitAsync(deadline.Token);
        Assert.Equal(1, node.Accepts);
        Assert.Equal(TimeSpan.FromSeconds(300), clock.Timers[^1].Due);
        Advance(clock, TimeSpan.FromSeconds(300));
        ProviderException failure = await Assert.ThrowsAsync<ProviderException>(() => call2);
        Assert.True(failure.IsTransient);
        Assert.Empty(failure.Detail);
    }

    [Fact]
    public async Task UpdatingReplyTimeLimitAppliesToReusedAndNewConnections()
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        ManualClock clock = new();
        TaskCompletionSource call2Received = Gate(), releaseCall2 = Gate();
        await using FakeNode node = FakeNode.Start(async (stream, ct) =>
        {
            _ = await ReadSegmentAsync(stream, ct);
            await SendReplyAsync(stream, 6, "8101", ct);
            _ = await ReadSegmentAsync(stream, ct);
            call2Received.TrySetResult();
            await releaseCall2.Task.WaitAsync(ct);
            await SendReplyAsync(stream, 6, "8101", ct);
            await DrainToEofAsync(stream, ct);
        });
        await using Ouroboros provider = new(node.SocketPath) { OnClientConnected = client => AttachClock(client, clock) };
        _ = await provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token);
        Assert.Equal(1, node.Accepts);
        Assert.Equal(TimeSpan.FromSeconds(180), clock.Timers[^1].Due);
        provider.ReplyTimeLimit = TimeSpan.FromSeconds(240);
        Task<string> call2 = provider.SubmitTransactionAsync(BlockfrostTests.Transaction, deadline.Token);
        await call2Received.Task.WaitAsync(deadline.Token);
        Assert.Equal(1, node.Accepts);
        Assert.Equal(TimeSpan.FromSeconds(240), clock.Timers[^1].Due);
        Advance(clock, TimeSpan.FromSeconds(200));
        Assert.False(call2.IsCompleted);
        releaseCall2.TrySetResult();
        Assert.NotEmpty(await call2);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-TimeSpan.TicksPerSecond)]
    [InlineData(-2L * TimeSpan.TicksPerMillisecond)]
    [InlineData(long.MinValue)]
    [InlineData(uint.MaxValue * TimeSpan.TicksPerMillisecond)]
    public void ReplyTimeLimitValidationRejectsInvalidValues(long ticks)
    {
        using Ouroboros provider = new("unused.sock");
        TimeSpan initial = TimeSpan.FromSeconds(42);
        provider.ReplyTimeLimit = initial;
        Assert.Equal("value", Assert.Throws<ArgumentOutOfRangeException>(() => provider.ReplyTimeLimit = new TimeSpan(ticks)).ParamName);
        Assert.Equal(initial, provider.ReplyTimeLimit);
        provider.ReplyTimeLimit = TimeSpan.FromMilliseconds(1);
        Assert.Equal(TimeSpan.FromMilliseconds(1), provider.ReplyTimeLimit);
        provider.ReplyTimeLimit = TimeSpan.FromMilliseconds(uint.MaxValue - 1);
        Assert.Equal(TimeSpan.FromMilliseconds(uint.MaxValue - 1), provider.ReplyTimeLimit);
        provider.ReplyTimeLimit = Timeout.InfiniteTimeSpan;
        Assert.Equal(Timeout.InfiniteTimeSpan, provider.ReplyTimeLimit);
    }

    [Fact]
    public async Task LateCancellationAfterSubmitAcceptanceReturnsTransactionId()
    {
        using CancellationTokenSource caller = new(TimeSpan.FromSeconds(10));
        string paramsWire = Convert.ToHexString(await File.ReadAllBytesAsync(Path.Combine(CliVectors.Root, CliVectors.Wire), caller.Token));
        ConcurrentQueue<string> followUpReplies = new(["8101", "820406", paramsWire]);
        await SubmitOnReplyDisposeAsync(
            (stream, ct) => ServeRepliesAsync(stream, followUpReplies, ct),
            _ => caller.Token,
            _ => CancelSync(caller),
            async (provider, node, ct) =>
            {
                Assert.True(caller.IsCancellationRequested);
                _ = await provider.GetParametersAsync(ct);
                Assert.Equal(1, node.Accepts);
            });
    }

    private static void AssertConnectionClosed(ProviderException failure, bool submit)
    {
        Assert.True(failure.IsTransient);
        Assert.Empty(failure.Detail);
        Assert.Null(failure.InnerException);
        Assert.Equal(submit
            ? "Ouroboros SubmitTransactionAsync: ConnectionClosedException; the transaction may have been accepted"
            : "Ouroboros GetParametersAsync: ConnectionClosedException", failure.Message);
    }

    private static async Task SubmitOnReplyDisposeAsync(
        Func<NetworkStream, CancellationToken, Task> afterReply, Func<CancellationTokenSource, CancellationToken> submitToken,
        Action<Ouroboros> onReplyDisposed, Func<Ouroboros, FakeNode, CancellationToken, Task> verify)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
        ManualClock clock = new();
        TaskCompletionSource requestReceived = Gate(), sendReply = Gate();
        await using FakeNode node = FakeNode.Start(async (stream, ct) =>
        {
            _ = await ReadSegmentAsync(stream, ct);
            requestReceived.TrySetResult();
            await sendReply.Task.WaitAsync(ct);
            await SendReplyAsync(stream, 6, "8101", ct);
            await afterReply(stream, ct);
        });
        await using Ouroboros provider = new(node.SocketPath) { OnClientConnected = client => AttachClock(client, clock) };
        Task<string> pending = provider.SubmitTransactionAsync(BlockfrostTests.Transaction, submitToken(deadline));
        await requestReceived.Task.WaitAsync(deadline.Token);
        clock.Timers[^1].OnDispose = () => onReplyDisposed(provider);
        sendReply.TrySetResult();
        Assert.Equal(((PostMaryTransaction)BlockfrostTests.Transaction).Body.Hash(), await pending, StringComparer.OrdinalIgnoreCase);
        await verify(provider, node, deadline.Token);
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void AssertClosedNow(Socket socket) => Assert.True(socket.Poll(0, SelectMode.SelectRead) && socket.Available == 0);

    private static async Task ReadConwayQueryAsync(Stream stream, CancellationToken cancellationToken)
    {
        _ = await ReadSegmentAsync(stream, cancellationToken);
        await SendReplyAsync(stream, 7, "8101", cancellationToken);
        _ = await ReadSegmentAsync(stream, cancellationToken);
        await SendReplyAsync(stream, 7, "820406", cancellationToken);
        _ = await ReadSegmentAsync(stream, cancellationToken);
    }

    private static async Task AcceptSubmitToEofAsync(Stream stream, CancellationToken cancellationToken)
    {
        _ = await ReadSegmentAsync(stream, cancellationToken);
        await SendReplyAsync(stream, 6, "8101", cancellationToken);
        await DrainToEofAsync(stream, cancellationToken);
    }

    private static void AttachClock(NodeClient client, ManualClock clock)
    {
        PropertyInfo? repliesProperty = typeof(NodeClient).GetProperty("Replies", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(repliesProperty);
        object? replies = repliesProperty.GetValue(client);
        Assert.NotNull(replies);
        PropertyInfo? clockProperty = replies.GetType().GetProperty("Clock", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(clockProperty);
        clockProperty.SetValue(replies, clock);
    }

    private static void Advance(ManualClock clock, TimeSpan elapsed)
    {
        ManualTimer[] due = [.. clock.Timers.Where(t => !t.Disposed && t.Due <= elapsed)];
        foreach (ManualTimer timer in due) { timer.Fire(); }
    }

    private static void DisposeSync(Ouroboros provider) => provider.Dispose();

    private static void CancelSync(CancellationTokenSource source) => source.Cancel();

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
            await using Ouroboros provider = new(path);
            Task running = RunCallAsync();
            async Task RunCallAsync() => await call(provider, deadline.Token);
            try
            {
                Task<Socket> accepting = listener.AcceptAsync(deadline.Token).AsTask();
                Assert.True(await Task.WhenAny(running, accepting) == accepting, "The call ended without connecting.");
                using Socket socket = await accepting;
                using NetworkStream stream = new(socket);
                Assert.Equal(0, (await ReadSegmentAsync(stream, deadline.Token))?.Protocol);
                await stream.WriteAsync(HandshakeAccept, deadline.Token);
                await node(stream, running, deadline.Token);
                Assert.False(deadline.IsCancellationRequested);
            }
            finally
            {
                await deadline.CancelAsync();
                try { await running; }
                catch (Exception) { }
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    // Answers each request with the next reply on its protocol, then returns every segment the call sent after the handshake.
    private static async Task<List<(int Protocol, byte[] Payload)>> ServeAsync(Func<Ouroboros, CancellationToken, Task> call, params string[] replies)
    {
        List<(int Protocol, byte[] Payload)> sent = [];
        await NodeAsync(async (p, ct) =>
        {
            try { await call(p, ct); }
            finally { await p.DisposeAsync(); }
        }, async (stream, running, ct) =>
        {
            foreach (string reply in replies)
            {
                (int protocol, byte[] payload) = await ReadSegmentAsync(stream, ct) ?? throw new InvalidOperationException("The call closed before its reply.");
                sent.Add((protocol, payload));
                await SendReplyAsync(stream, protocol, reply, ct);
            }
            await running;
            while (await ReadSegmentAsync(stream, ct) is { } rest) { sent.Add(rest); }
        });
        return sent;
    }

    private static async Task SendReplyAsync(Stream stream, int protocol, string hexPayload, CancellationToken cancellationToken)
    {
        byte[] body = Convert.FromHexString(hexPayload);
        byte[] header = new byte[8];
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)(protocol | 0x8000));
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(6), (ushort)body.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
    }

    private static async Task ServeRepliesAsync(NetworkStream stream, ConcurrentQueue<string> replies, CancellationToken cancellationToken)
    {
        while (await ReadSegmentAsync(stream, cancellationToken) is { } request)
        {
            // LocalStateQuery Release(5) expects no reply (LocalStateQuery.cs:138-145).
            if (request.Protocol == 7 && request.Payload is [0x81, 0x05]) { continue; }
            Assert.True(replies.TryDequeue(out string? reply), "No queued reply left for incoming request.");
            await SendReplyAsync(stream, request.Protocol, reply, cancellationToken);
        }
    }

    private static async Task DrainToEofAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] drain = new byte[64];
        while (await stream.ReadAsync(drain, cancellationToken) != 0)
        {
            // Any already queued protocol bytes can precede the close.
        }
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

    private sealed class FakeNode : IAsyncDisposable
    {
        private readonly DirectoryInfo _directory;
        private readonly Socket _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly Func<int, NetworkStream, CancellationToken, Task> _handleConnection;
        private readonly bool _acceptHandshake;
        private readonly Lock _gate = new();
        private readonly List<Task> _handlers = [];
        private readonly Task _acceptLoop;
        private readonly TaskCompletionSource _closedBeforeHandshake = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _accepts;

        internal string SocketPath { get; }
        internal int Accepts => Volatile.Read(ref _accepts);
        internal Task ClosedBeforeHandshake => _closedBeforeHandshake.Task;

        private FakeNode(Func<int, NetworkStream, CancellationToken, Task> handleConnection, bool acceptHandshake)
        {
            _handleConnection = handleConnection;
            _acceptHandshake = acceptHandshake;
            _directory = Directory.CreateTempSubdirectory("t12b");
            SocketPath = Path.Combine(_directory.FullName, "n.sock");
            _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(SocketPath));
            _listener.Listen(8);
            _acceptLoop = AcceptLoopAsync();
        }

        internal static FakeNode Start(Func<int, NetworkStream, CancellationToken, Task> handleConnection, bool acceptHandshake = true) =>
            new(handleConnection, acceptHandshake);

        internal static FakeNode Start(Func<NetworkStream, CancellationToken, Task> handleConnection, bool acceptHandshake = true) =>
            new((_, stream, ct) => handleConnection(stream, ct), acceptHandshake);

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    Socket socket = await _listener.AcceptAsync(_stop.Token);
                    int number = Interlocked.Increment(ref _accepts);
                    Task handler = HandleSocketAsync(number, socket);
                    lock (_gate) { _handlers.Add(handler); }
                }
            }
            catch (Exception error) when (_stop.IsCancellationRequested && error is OperationCanceledException or SocketException or ObjectDisposedException) { }
        }

        private async Task HandleSocketAsync(int number, Socket socket)
        {
            try
            {
                using (socket)
                using (NetworkStream stream = new(socket))
                {
                    if (await ReadSegmentAsync(stream, _stop.Token) is not { } handshake) { _closedBeforeHandshake.TrySetResult(); return; }
                    Assert.Equal(0, handshake.Protocol);
                    if (_acceptHandshake) { await stream.WriteAsync(HandshakeAccept, _stop.Token); }
                    await _handleConnection(number, stream, _stop.Token);
                }
            }
            catch (Exception error) when (_stop.IsCancellationRequested && error is OperationCanceledException or IOException or SocketException or ObjectDisposedException) { }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Dispose();
            await _acceptLoop;
            Task[] handlers;
            lock (_gate) { handlers = [.. _handlers]; }
            await Task.WhenAll(handlers);
            _stop.Dispose();
            _directory.Delete(recursive: true);
        }
    }
}
