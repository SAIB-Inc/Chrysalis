using System.Buffers;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Tx.Providers;
using Chrysalis.Tx.Providers.Lean;
using Google.Protobuf;
using Grpc.Core;
using Perfolizer.Metrology;
using Utxorpc.V1Beta.Query;

_ = BenchmarkSwitcher.FromTypes([typeof(UtxoRpcSearchBenchmarks), typeof(UtxoRpcParameterBenchmarks)]).Run(args,
    DefaultConfig.Instance.AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance)).AddExporter(JsonExporter.Full).WithSummaryStyle(SummaryStyle.Default.WithCultureInfo(CultureInfo.InvariantCulture).WithSizeUnit(SizeUnit.B)));

/// <summary>Compares shipped lean parsing with full parsing on identical recorded pages.</summary>
[MemoryDiagnoser]
public class UtxoRpcSearchBenchmarks
{
    private const string Recorded = "Recorded";
    private PayloadContext _input = null!;
    private readonly LeanUtxo _projection = new();

    /// <summary>Gets or sets the recorded page or its repeated item count.</summary>
    [Params(Recorded, "100", "1000")]
    public string Input { get; set; } = Recorded;

    /// <summary>Loads the one embedded capture and builds the selected repeated page once.</summary>
    [GlobalSetup]
    public void Setup()
    {
        byte[] bytes = PayloadContext.ReadResource("search-utxos.pb");
        SearchUtxosResponse original = SearchUtxosResponse.Parser.ParseFrom(new ReadOnlySequence<byte>(bytes));
        if (original.Items.Count == 0) { throw new InvalidDataException("The recorded page has no outputs."); }
        if (Input != Recorded)
        {
            SearchUtxosResponse repeated = original.Clone();
            repeated.Items.Clear();
            int count = int.Parse(Input, CultureInfo.InvariantCulture);
            for (int i = 0; i < count; i++) { repeated.Items.Add(original.Items[i % original.Items.Count]); }
            bytes = repeated.ToByteArray();
        }
        _input = new PayloadContext(bytes);
    }

    /// <summary>Runs the shipped sequence deserializer and decodes every lean output.</summary>
    /// <returns>The last decoded output, retaining a dependency on the measured work.</returns>
    [Benchmark]
    public ResolvedInput SearchUtxosLean()
    {
        LeanUtxosResponse response = UtxoRpcMethods.V1Beta.SearchUtxos.ResponseMarshaller.ContextualDeserializer(_input);
        ResolvedInput result = null!;
        for (int i = 0; i < response.Items.Count; i++) { result = UtxoRpc.ToResolvedInput(response.Items[i]); }
        return result;
    }

    /// <summary>Parses the full spec response on the same sequence, then performs the same decodes.</summary>
    /// <returns>The last decoded output.</returns>
    [Benchmark]
    public ResolvedInput SearchUtxosFull()
    {
        SearchUtxosResponse response = SearchUtxosResponse.Parser.ParseFrom(_input.PayloadAsReadOnlySequence());
        ResolvedInput result = null!;
        for (int i = 0; i < response.Items.Count; i++)
        {
            // Reuse the projection: full parsing is not charged an extra wrapper per item.
            _projection.NativeBytes = response.Items[i].NativeBytes;
            _projection.TxoRef = response.Items[i].TxoRef;
            result = UtxoRpc.ToResolvedInput(_projection);
        }
        return result;
    }
}

/// <summary>Measures the recorded parameter response through the shipped parse and mapping.</summary>
[MemoryDiagnoser]
public class UtxoRpcParameterBenchmarks
{
    private PayloadContext _input = null!;

    /// <summary>Loads the recorded parameters once, outside measured work.</summary>
    [GlobalSetup]
    public void Setup() => _input = new PayloadContext(PayloadContext.ReadResource("read-params.pb"));

    /// <summary>Runs the shipped parameter deserializer and ledger mapping.</summary>
    /// <returns>The mapped parameters.</returns>
    [Benchmark]
    public ProtocolParams ReadParams()
    {
        ReadParamsResponse response = UtxoRpcMethods.V1Beta.ReadParams.ResponseMarshaller.ContextualDeserializer(_input);
        return UtxoRpcParameters.Map(response.Values.Cardano);
    }
}

/// <summary>Supplies recorded bytes through the same sequence accessor gRPC uses.</summary>
internal sealed class PayloadContext(byte[] bytes) : DeserializationContext
{
    private readonly ReadOnlySequence<byte> _payload = new(bytes);
    /// <inheritdoc />
    public override int PayloadLength => checked((int)_payload.Length);
    /// <inheritdoc />
    public override ReadOnlySequence<byte> PayloadAsReadOnlySequence() => _payload;
    /// <inheritdoc />
    public override byte[] PayloadAsNewBuffer() => throw new NotSupportedException("The benchmark uses the sequence deserializer.");

    internal static byte[] ReadResource(string name)
    {
        using Stream stream = typeof(PayloadContext).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidDataException($"Missing embedded capture: {name}.");
        byte[] bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return bytes;
    }
}
