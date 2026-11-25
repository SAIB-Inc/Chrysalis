using System.Buffers;
using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.Emit;
using System.IO.Pipelines;
using System.Security.Cryptography;
using BenchmarkDotNet.Attributes;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Chrysalis.Codec.Types.Cardano.Core.TransactionWitness;
using Chrysalis.Codec.Types.Cardano.Core.Scripts;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Extensions.Cardano.Core;
using Chrysalis.Codec.Extensions.Cardano.Core.Header;
using Chrysalis.Codec.Extensions.Cardano.Core.Transaction;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.LocalStateQuery;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.Core;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Extensions;
#if CHRYSALIS_PINNED_HARNESS
using Chrysalis.Tx.Utils;
#else
using Chrysalis.Tx.Fees;
using Chrysalis.Tx.Models.Cbor;
using Chrysalis.Wallet.Models.Keys;
#endif
using Chrysalis.Wallet.Utils;
using Chrysalis.Crypto;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Types;
using SAIB.Cbor.Serialization;
using Chrysalis.Codec.Types.Cardano.Core.Header;

namespace Chrysalis.Benchmarks;

/// <summary>Measures CoreBench operations using fixed public fixtures.</summary>
[MemoryDiagnoser]
public class CoreBench
{
    internal readonly byte[] intBytes = [0x18, 0x2A], headerBytes = new byte[8];
    internal readonly byte[] payload = [.. Enumerable.Range(0, 1024).Select(i => (byte)i)];
    internal byte[] mem = null!, memIndef = null!, inputBytes = null!, outputBytes = null!, msg = null!, key = null!, pub = null!, sig = null!, address = null!;
    internal string addrText = null!;
    internal TransactionInput input; internal MuxSegment small = null!, large = null!;
    internal ArrayBufferWriter<byte> writer = new(2048);
#if !CHRYSALIS_PINNED_HARNESS
    internal PublicKey derivePublicKey = new([], []);
    internal Func<PublicKey, int, PublicKey>? derivePublicFn;
#endif
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [GlobalSetup]
    public void Setup()
    {
        ArrayBufferWriter<byte> w = new(); CborWriter cw = new(w); cw.WriteByteString(payload); mem = w.WrittenSpan.ToArray();
        memIndef = new byte[1032]; memIndef[0] = 0x5F; memIndef[1] = memIndef[516] = 0x59; memIndef[2] = memIndef[517] = 2; payload.AsSpan(0, 512).CopyTo(memIndef.AsSpan(4)); payload.AsSpan(512).CopyTo(memIndef.AsSpan(519)); memIndef[^1] = 0xFF;
        input = TransactionInput.Create(payload.AsMemory(0, 32), 0); inputBytes = CborSerializer.Serialize(input);
        address = new byte[57]; address[0] = 0; payload.AsSpan(0, 56).CopyTo(address.AsSpan(1)); addrText = Bech32Util.Encode(address, "addr_test");
        outputBytes = CborSerializer.Serialize(AlonzoTransactionOutput.Create(Address.Create(address), Lovelace.Create(5000000), null));
        msg = payload.AsSpan(0, 32).ToArray(); byte[] seed = RandomNumberGenerator.GetBytes(32); key = Ed25519.ExpandedPrivateKeyFromSeed(seed); CryptographicOperations.ZeroMemory(seed); pub = key.AsSpan(32, 32).ToArray(); sig = Ed25519.Sign(msg, key); if (!Ed25519.Verify(sig, msg, pub))
        {
            throw new InvalidOperationException("Generated-key signature failed verification");
        }

#if !CHRYSALIS_PINNED_HARNESS
        derivePublicKey = new PublicKey(Ed25519.ExpandedPrivateKeyFromSeed(msg)[32..], payload.AsSpan(32, 32).ToArray());
        derivePublicFn = typeof(PublicKey).GetMethod("Derive", [typeof(int)])?.CreateDelegate<Func<PublicKey, int, PublicKey>>();
#endif
        small = new(new(0, ProtocolType.NodeChainSync, 32, false), new ReadOnlySequence<byte>(payload.AsMemory(0, 32)));
        large = new(new(0, ProtocolType.NodeChainSync, 1024, false), new ReadOnlySequence<byte>(payload));
    }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ulong PrimitiveIntRead() => CborSerializer.Deserialize<ulong>(intBytes);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ReadOnlyMemory<byte> PrimitiveMemoryRead() => CborSerializer.Deserialize<ReadOnlyMemory<byte>>(mem);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ReadOnlyMemory<byte> PrimitiveMemoryConsumedRead() => CborSerializer.Deserialize<ReadOnlyMemory<byte>>(mem, out _);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ReadOnlyMemory<byte> PrimitiveIndefMemoryRead() { CborReader r = new(memIndef); return r.ReadByteStringToArray(); }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ReadOnlyMemory<byte> PrimitiveMemoryBorrowedAlternative() { CborReader r = new(mem); ReadOnlySpan<byte> bytes = r.ReadByteString(); return mem.AsMemory(mem.Length - r.Buffer.Length - bytes.Length, bytes.Length); }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public int PrimitiveDirectRead() { CborReader r = new(mem); return r.ReadByteString().Length; }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public TransactionInput GeneratedInputRead() => CborSerializer.Deserialize<TransactionInput>(inputBytes);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ulong GeneratedInputAccess() { TransactionInput v = CborSerializer.Deserialize<TransactionInput>(inputBytes); return v.Index + (ulong)v.TransactionId.Length; }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public TransactionInput GeneratedInputCreate() => TransactionInput.Create(payload.AsMemory(0, 32), 0);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public TransactionInput InputCreateNoFinalCopy() { ArrayBufferWriter<byte> w = new(); CborWriter cw = new(w); cw.WriteBeginArray(2); cw.WriteByteString(payload.AsSpan(0, 32)); cw.WriteUInt64(0); return TransactionInput.Read(w.WrittenMemory); }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ITransactionOutput GeneratedUnionRead() => CborSerializer.Deserialize<ITransactionOutput>(outputBytes);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public AlonzoTransactionOutput GeneratedConcreteRead() => CborSerializer.Deserialize<AlonzoTransactionOutput>(outputBytes);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public byte[] SerializeRawInput() => CborSerializer.Serialize(input);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ReadOnlyMemory<byte> SerializeRawInputMemory() => CborSerializer.SerializeToMemory(input);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ReadOnlySequence<byte> MuxEncode32() => MuxSegmentCodec.Encode(small);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ReadOnlySequence<byte> MuxEncode1024() => MuxSegmentCodec.Encode(large);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public int MuxEncodeIntoReusableWriter() { writer.Clear(); Span<byte> dst = writer.GetSpan(1032); BinaryPrimitives.WriteUInt32BigEndian(dst, large.Header.TransmissionTime); BinaryPrimitives.WriteUInt16BigEndian(dst[4..], (ushort)large.Header.ProtocolId); BinaryPrimitives.WriteUInt16BigEndian(dst[6..], 1024); payload.CopyTo(dst[8..]); writer.Advance(1032); return writer.WrittenCount; }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public MuxSegmentHeader MuxDecodeHeader() => MuxSegmentCodec.DecodeHeader(new ReadOnlySequence<byte>(headerBytes));
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public string AddressEncode() => Bech32Util.Encode(address, "addr_test");
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public byte[] AddressDecode() => Bech32Util.Decode(addrText).data;
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public byte[] Blake256() => HashUtil.Blake2b256(payload);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public byte[] Sign() => Ed25519.Sign(msg, key);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public bool Verify() => Ed25519.Verify(sig, msg, pub);
#if !CHRYSALIS_PINNED_HARNESS
    /// <summary>Derives a soft child public key from a fixed public key and chain code.</summary>
    [Benchmark] public PublicKey DerivePublic() => (derivePublicFn ?? throw new InvalidOperationException("PublicKey.Derive(int) is unavailable on this commit."))(derivePublicKey, 0);
#endif
}

/// <summary>Measures BlockBench operations using fixed public fixtures.</summary>
[MemoryDiagnoser]
public class BlockBench
{
    private byte[][] blocks = null!, txs = null!, envelopes = null!; private BlockWithEra block; private ConwayTransactionBody tx; private PostMaryTransaction envelope;
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [GlobalSetup]
    public void Setup()
    {
        envelopes = [.. Directory.GetFiles(Path.Combine(Fixtures.Root, "fixtures"), "tx-*.cbor").Order().Select(File.ReadAllBytes)]; if (envelopes.Length > 0)
        {
            envelope = CborSerializer.Deserialize<PostMaryTransaction>(envelopes[0]);
        }

        blocks = [.. Directory.GetFiles(Path.Combine(Fixtures.Root, "fixtures"), "block-*.cbor").Order().Select(File.ReadAllBytes)]; txs = [.. Directory.GetFiles(Path.Combine(Fixtures.Root, "fixtures"), "txbody-*.cbor").Order().Select(File.ReadAllBytes)]; if (blocks.Length == 0)
        {
            blocks = [Convert.FromHexString(File.ReadAllText(Path.Combine(Fixtures.Root, "../data/conway1.block")).Trim())];
        }

        block = CborSerializer.Deserialize<BlockWithEra>(blocks[0]); if (txs.Length > 0)
        {
            tx = CborSerializer.Deserialize<ConwayTransactionBody>(txs[0]);
        }
    }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public BlockWithEra DecodeBlockLazy() => CborSerializer.Deserialize<BlockWithEra>(blocks[0]);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public long DecodeBlockSetLazy() { long n = 0; foreach (byte[] b in blocks) { n += CborSerializer.Deserialize<BlockWithEra>(b).Raw.Length; } return n; }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ulong DecodeBlockReadInputs() { BlockWithEra b = CborSerializer.Deserialize<BlockWithEra>(blocks[0]); ulong n = 0; foreach (ITransactionBody t in b.Block.TransactionBodies()) { foreach (TransactionInput i in t.Inputs()) { n += i.Index + (ulong)i.TransactionId.Length; } } return n; }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public byte[] ReencodeRawBlock() => CborSerializer.Serialize(block);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ReadOnlyMemory<byte> ReencodeRawBlockMemory() => CborSerializer.SerializeToMemory(block);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public byte[] ReencodeConwayContainer() { ConwayBlock c = (ConwayBlock)block.Block; return CborSerializer.Serialize(ConwayBlock.Create(c.Header, c.TransactionBodies, c.TransactionWitnessSets, c.AuxiliaryDataSet, c.InvalidTransactions)); }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ConwayTransactionBody DecodeTxBodyLazy() => CborSerializer.Deserialize<ConwayTransactionBody>(txs[0]);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public int DecodeTxSetLazy() { int n = 0; foreach (byte[] t in txs) { n += CborSerializer.Deserialize<ConwayTransactionBody>(t).Raw.Length; } return n; }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public byte[] ReencodeRawTxBody() => CborSerializer.Serialize(tx);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public PostMaryTransaction DecodeTransactionLazy() => CborSerializer.Deserialize<PostMaryTransaction>(envelopes[0]);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public byte[] ReencodeRawTransaction() => CborSerializer.Serialize(envelope);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ulong DecodeTransactionInputs() { PostMaryTransaction t = CborSerializer.Deserialize<PostMaryTransaction>(envelopes[0]); ulong n = 0; foreach (TransactionInput i in t.Body.Inputs()) { n += i.Index + (ulong)i.TransactionId.Length; } return n; }
}

/// <summary>Measures TxBench operations using fixed public fixtures.</summary>
[MemoryDiagnoser]
public class TxBench
{
    internal CoreBench core = new(); internal ProtocolParams pp = null!; internal PlutusV3Script script; internal IRedeemers redeemers = null!;
    internal Tx.Models.RationalNumber step = new(721, 10000000), memory = new(577, 10000);
#if !CHRYSALIS_PINNED_HARNESS
    private PostMaryTransaction transferDraft, scriptDraft, referenceDraft;
    private FeeInputs transferInputs, scriptInputs, referenceInputs;
    private FeeDraft prepared;
#endif
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [GlobalSetup]
    public void Setup()
    {
        core.Setup(); pp = new(44, 155381, null, 16384, null, null, null, null, null, null, null, null, null, null, 4310, new CostMdls(null, null,
#if CHRYSALIS_PINNED_HARNESS
            CborDefList<long>.Create([0, 1, 2])
#else
            CborList.Create<long>([0, 1, 2])
#endif
        ), ExUnitPrices.Create(new CborRationalNumber(577, 10000), new CborRationalNumber(721, 10000000)), null, null, null, 150, 3, null, null, null, null, null, null, null, null, new CborRationalNumber(15, 1));
        if (File.Exists(Path.Combine(Fixtures.Root, "fixtures/protocol-parameters.cbor")))
        {
            pp = CborSerializer.Deserialize<CurrentProtocolParamsResponse>(File.ReadAllBytes(Path.Combine(Fixtures.Root, "fixtures/protocol-parameters.cbor"))).ProtocolParams;
        }

        script = PlutusV3Script.Create(3, File.ReadAllBytes(Path.Combine(Fixtures.Root, "../data/plutus_use_cases/escrow-redeem_1-1.flat"))); redeemers = RedeemerList.Create([RedeemerEntry.Create(0, 0, PlutusInt.Create(1), ExUnits.Create(1000, 2000))]);
#if !CHRYSALIS_PINNED_HARNESS
        Address address = Address.Create(core.address);
        ITransactionOutput change = AlonzoTransactionOutput.Create(address, Lovelace.Create(4_800_000), null);
        transferDraft = NewBuilder(false).AddOutput(change).Build();
        scriptDraft = NewBuilder(true).AddOutput(change).Build();
        TransactionInput scriptInput = TransactionInput.Create(core.payload.AsMemory(64, 32), 0);
        ITransactionOutput scriptOutput = PostAlonzoTransactionOutput.Create(address, Lovelace.Create(2_000_000), scriptRef: new CborEncodedValue(script.Raw));
        referenceDraft = NewBuilder(true, useReference: true).AddReferenceInput(scriptInput).AddOutput(change).Build();
        ulong referenceSize = FeeEngine.ReferenceScriptSize([], [scriptInput], [new ResolvedInput(scriptInput, scriptOutput)]);
        transferInputs = new FeeInputs(ChangeIndex: 1, Budget: 5_000_000, SigningDelta: SigningDelta(transferDraft));
        scriptInputs = transferInputs with { CollateralBalance = 5_000_000, SigningDelta = SigningDelta(scriptDraft) };
        referenceInputs = scriptInputs with { ReferenceScriptSize = referenceSize, SigningDelta = SigningDelta(referenceDraft) };
        prepared = FeeSolver.Prepare(transferDraft, pp, transferInputs);
        CheckBalanced(prepared, $"{nameof(FeeTransfer)}/{nameof(FeeSolve)}");
        CheckBalanced(FeeSolver.Prepare(scriptDraft, pp, scriptInputs), nameof(FeeScriptSpend));
        CheckBalanced(FeeSolver.Prepare(referenceDraft, pp, referenceInputs), nameof(FeeReferenceScript));
#endif
    }
#if !CHRYSALIS_PINNED_HARNESS
    private static void CheckBalanced(in FeeDraft draft, string benchmark)
    {
        FeeStatus status = FeeSolver.Solve(draft).Status;
        if (status != FeeStatus.Balanced)
        {
            throw new InvalidOperationException($"{benchmark} requires a balanced fee draft; got {status}.");
        }
    }
    private ulong SigningDelta(PostMaryTransaction draft)
    {
        PostMaryTransaction signed = (PostMaryTransaction)draft.Sign([VKeyWitness.Create(core.pub, new byte[64])]);
        return checked((ulong)(signed.Witnesses.Raw.Length - draft.Witnesses.Raw.Length));
    }
#endif
    private TransactionBuilder NewBuilder(bool scripted, bool useReference = false)
    {
        TransactionBuilder b = TransactionBuilder.Create(pp); Address addr = Address.Create(core.address);
        TransactionInput input = TransactionInput.Create(core.payload.AsMemory(0, 32), 0);
        if (scripted)
        {
            InputBuilder spend = new(input, AlonzoTransactionOutput.Create(addr, Lovelace.Create(10000000), null));
            _ = b.AddInput(useReference ? spend.PlutusScriptRef(script.HashHex(), PlutusInt.Create(0), PlutusInt.Create(1))
                : spend.PlutusScript(script, PlutusInt.Create(0), PlutusInt.Create(1)));
        }
        else
        {
            _ = b.AddInput(input);
        }

        _ = b.AddOutput(AlonzoTransactionOutput.Create(addr, Lovelace.Create(5000000), null)).SetFee(200000);
        if (scripted) { _ = b.SetRedeemers(redeemers); _ = b.AddCollateral(TransactionInput.Create(core.payload.AsMemory(32, 32), 1)); }
        return b;
    }
    internal byte[] BuildSign(bool scripted, bool fee)
    {
        TransactionBuilder b = NewBuilder(scripted); if (fee)
        {
            _ = b.CalculateFee(scripted ? [script] : [], mockWitnessFee: 1);
        }

        ConwayTransactionBody body = b.BuildBody(); byte[] hash = Blake2Fast.Blake2b.HashData(32, body.Raw.Span); byte[] sig = Ed25519.Sign(hash, core.key);
        _ = b.AddVKeyWitness(VKeyWitness.Create(core.pub, sig)); return CborSerializer.Serialize(b.Build());
    }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public byte[] SimpleBuildSign() => BuildSign(false, false);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public byte[] SimpleBuildFeeSign() => BuildSign(false, true);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public byte[] ScriptBuildFeeSign() => BuildSign(true, true);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
#if CHRYSALIS_PINNED_HARNESS
    [Benchmark] public ulong ScriptFee() => FeeUtil.CalculateScriptExecutionFee(redeemers, step, memory);
#else
    [Benchmark] public ulong ScriptFee() => FeeEngine.ScriptFee(redeemers, pp);
#endif
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ulong ScriptFeeLoop() { ulong fee = 0; foreach (RedeemerEntry x in ((RedeemerList)redeemers).Value) { fee += (x.ExUnits.Mem * memory.Numerator / memory.Denominator) + (x.ExUnits.Steps * step.Numerator / step.Denominator); } return fee; }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
#if CHRYSALIS_PINNED_HARNESS
    [Benchmark] public ulong LinearFee() => FeeUtil.CalculateFeeWithWitness(300, pp.MinFeeA!.Value, pp.MinFeeB!.Value, 1);
#else
    [Benchmark] public ulong LinearFee() => FeeEngine.LinearFee(300, pp);
#endif
#if !CHRYSALIS_PINNED_HARNESS
    /// <summary>Prepares and solves a transfer with change.</summary>
    [Benchmark] public ulong FeeTransfer() => FeeSolver.Solve(FeeSolver.Prepare(transferDraft, pp, transferInputs)).Fee;
    /// <summary>Prepares and solves a Plutus spend with an attached script.</summary>
    [Benchmark] public ulong FeeScriptSpend() => FeeSolver.Solve(FeeSolver.Prepare(scriptDraft, pp, scriptInputs)).Fee;
    /// <summary>Prepares and solves a Plutus spend with a resolved reference script.</summary>
    [Benchmark] public ulong FeeReferenceScript() => FeeSolver.Solve(FeeSolver.Prepare(referenceDraft, pp, referenceInputs)).Fee;
    /// <summary>Solves a prepared transfer without draft-building or preparation costs.</summary>
    [Benchmark] public ulong FeeSolve() => FeeSolver.Solve(prepared).Fee;
#endif
}

/// <summary>Measures VmBench operations using fixed public fixtures.</summary>
[MemoryDiagnoser]
public class VmBench
{
    private readonly ExBudget budget = ExBudget.Unlimited;
    private byte[] data = null!; private Program<DeBruijn> program = null!; private object sharedCosts = null!; private Func<ExBudget, object, CekMachine> sharedFactory = null!;
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [GlobalSetup]
    public void Setup()
    {
        data = File.ReadAllBytes(Path.Combine(Fixtures.Root, "../data/plutus_use_cases/escrow-redeem_1-1.flat")); program = FlatDecoder.DecodeProgram(data);
        Assembly a = typeof(CekMachine).Assembly; Type costsType = a.GetType("Chrysalis.Plutus.Cek.BuiltinCostModel")!.MakeArrayType(); sharedCosts = a.GetType("Chrysalis.Plutus.Cek.DefaultCosts")!.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!;
        ConstructorInfo ctor = typeof(CekMachine).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(ExBudget), costsType], null)!;
        DynamicMethod dm = new("SharedCostsMachine", typeof(CekMachine), [typeof(ExBudget), typeof(object)], typeof(VmBench).Module, true); ILGenerator il = dm.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Castclass, costsType); il.Emit(OpCodes.Newobj, ctor); il.Emit(OpCodes.Ret); sharedFactory = (Func<ExBudget, object, CekMachine>)dm.CreateDelegate(typeof(Func<ExBudget, object, CekMachine>));
    }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public Term<DeBruijn> DecodeAndEvaluate() => new CekMachine(ExBudget.Unlimited).Run(FlatDecoder.DecodeProgram(data).Term);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public Term<DeBruijn> EvaluateOnly() => new CekMachine(ExBudget.Unlimited).Run(program.Term);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public CekMachine CreateMachine() => new(budget);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public CekMachine CreateMachineSharedCosts() => sharedFactory(ExBudget.Unlimited, sharedCosts);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public Term<DeBruijn> EvaluateSharedCosts() => sharedFactory(ExBudget.Unlimited, sharedCosts).Run(program.Term);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public Program<DeBruijn> FlatDecode() => FlatDecoder.DecodeProgram(data);
}

/// <summary>Measures LoopBearer operations using fixed public fixtures.</summary>
public sealed class LoopBearer : IBearer
{
    private readonly Pipe pipe = new();
    private bool disposed;
    /// <inheritdoc/>
    public PipeReader Reader => pipe.Reader;
    /// <inheritdoc/>
    public PipeWriter Writer => pipe.Writer;
    /// <inheritdoc/>
    public void Dispose() { if (disposed) { return; } disposed = true; pipe.Reader.Complete(); pipe.Writer.Complete(); }
}
/// <summary>Measures MuxBench operations using fixed public fixtures.</summary>
[MemoryDiagnoser]
public class MuxBench : IDisposable
{
    private Plexer plexer = null!; private AgentChannel channel = null!; private CancellationTokenSource cts = null!; private Task task = null!;
    private bool disposed;
    private LoopBearer bearer = null!;
    private readonly byte[] small = new byte[32], large = new byte[1024];
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [GlobalSetup] public void Setup() { bearer = new(); plexer = new(bearer); channel = plexer.SubscribeClient(ProtocolType.NodeChainSync); cts = new(); task = plexer.RunAsync(cts.Token); }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public int Loop32() => Roundtrip(small);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public int Loop1024() => Roundtrip(large);
    private int Roundtrip(byte[] payload) { channel.EnqueueChunkAsync(new ReadOnlySequence<byte>(payload)).GetAwaiter().GetResult(); return channel.ReadSegmentAsync().AsTask().GetAwaiter().GetResult().Length; }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [GlobalCleanup] public void Cleanup() => Dispose();
    /// <inheritdoc/>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Stops the in-memory transport and releases owned resources.</summary>
    /// <param name="disposing">Whether managed resources may be disposed.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing || disposed) { return; }
        disposed = true;
        cts.Cancel();
        try { task.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) { }
        plexer.Dispose();
        bearer.Dispose();
        cts.Dispose();
    }
}

/// <summary>Measures CorpusVmBench operations using fixed public fixtures.</summary>
[MemoryDiagnoser]
public class CorpusVmBench
{
    private byte[][] data = null!;
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [GlobalSetup]
    public void Setup()
    {
        data = [.. Directory.GetFiles(Path.Combine(Fixtures.Root, "../data/plutus_use_cases"), "*.flat").Order().Select(File.ReadAllBytes)]; if (data.Length != 78)
        {
            throw new InvalidOperationException("Expected exactly 78 repository scripts");
        }
    }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark(OperationsPerInvoke = 78)] public int DecodeEvaluateAll78() { int n = 0; foreach (byte[] d in data) { Program<DeBruijn> p = FlatDecoder.DecodeProgram(d); Term<DeBruijn>? term = new CekMachine(ExBudget.Unlimited).Run(p.Term); if (term is not null) { n++; } } return n; }
}

/// <summary>Measures NetworkHeaderBench operations using fixed public fixtures.</summary>
[MemoryDiagnoser]
public class NetworkHeaderBench
{
    private byte[] payload = null!; private ChainSyncHeader decoded = null!;
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [GlobalSetup] public void Setup() { BlockWithEra block = CborSerializer.Deserialize<BlockWithEra>(File.ReadAllBytes(Directory.GetFiles(Path.Combine(Fixtures.Root, "fixtures"), "block-*.cbor").Order().First())); ReadOnlyMemory<byte> hdr = block.Block.Header().Raw; payload = new byte[hdr.Length + 7]; payload[0] = 0x82; payload[1] = 6; payload[2] = 0xD8; payload[3] = 0x18; payload[4] = 0x59; payload[5] = (byte)(hdr.Length >> 8); payload[6] = (byte)hdr.Length; hdr.Span.CopyTo(payload.AsSpan(7)); decoded = ChainSyncHeader.Decode(payload); }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ChainSyncHeader DecodeCurrent() => ChainSyncHeader.Decode(payload);
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ChainSyncHeader DecodeBorrowedAlternative() { CborReader r = new(payload); r.ReadBeginArray(); _ = r.ReadSize(); byte variant = (byte)r.ReadUInt64(); _ = r.TryReadSemanticTag(out _); ReadOnlySpan<byte> bytes = r.ReadByteString(); return new(variant, null, payload.AsMemory(payload.Length - r.Buffer.Length - bytes.Length, bytes.Length)); }
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ChainPoint ExtractPointCurrent() => decoded.ExtractPoint();
    /// <summary>Runs the named benchmark operation or its untimed setup/cleanup.</summary>
    [Benchmark] public ChainPoint ExtractPointDirectHashAlternative() { BlockHeader header = CborSerializer.Deserialize<Codec.Types.Cardano.Core.Header.BlockHeader>(decoded.HeaderCbor); IBlockHeaderBody body = header.HeaderBody; return new(body.Slot(), Blake2Fast.Blake2b.HashData(32, decoded.HeaderCbor.Span), body.BlockNumber()); }
}
