using System.Buffers;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Codec.Extensions.Cardano.Core.Certificates;
using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core;
using Chrysalis.Codec.Types.Cardano.Core.Certificates;
using Chrysalis.Codec.Types.Cardano.Core.Governance;
using Chrysalis.Codec.Types.Cardano.Core.Protocol;
using Chrysalis.Codec.Types.Cardano.Core.Transaction;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Target = Chrysalis.Codec.Types.Cardano.Core.Certificates.Target;

namespace Chrysalis.Codec.Test;

// Top-level synthetic unions for CborRecord class-case integrity and zero-progress manual reader guard.
[CborSerializable, CborUnion(typeof(CborClassCase), typeof(OtherClassCase)), Union]
public readonly partial struct SyntheticClassCaseUnion : ICborType;

[CborSerializable, CborList, CborIndex(0)]
public partial record CborClassCase([CborOrder(0)] int Tag) : CborRecord;

[CborSerializable, CborMap]
public partial record OtherClassCase([CborProperty(0)] int Value) : CborRecord;

[CborSerializable, CborUnion(typeof(ZeroProgressManualCase)), Union]
public readonly partial struct ZeroProgressManualUnion : ICborType;

public readonly record struct ZeroProgressManualCase : ICborType
{
    public ReadOnlyMemory<byte> Raw => default;
    public int ConstrIndex => 0;
    public bool IsIndefinite => false;
    public static ZeroProgressManualCase Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        _ = data;
        bytesConsumed = 0;
        return default;
    }
    public static void Write(IBufferWriter<byte> output, ZeroProgressManualCase value)
    {
        _ = output;
        _ = value;
    }
}

public class OfficialUnionFirstFamilyTests
{
    // Provenance for D17 independent oracles:
    // - Nonce & Target (MoveInstantaneousReward): derived from IntersectMBO/cardano-ledger
    //   (commits 95b369053fd32422695a81fb7d5289d6dd35eee2 & 1d78754a701073420ab1b36a315d487cbdba9153, Apache-2.0,
    //   TestData/X7Attribution/{LICENSE,NOTICE-cardano-ledger}):
    //   * eras/allegra/impl/cddl/data/allegra.cddl:262 (`nonce = [0 // 1, hash32]`),
    //     sha256 e71cbf08f4fe62bb654f8371f4934eba750c17546b7d44c0c4f38b470eaabcd6
    //   * eras/allegra/impl/cddl/data/allegra.cddl:223 (`move_instantaneous_reward = [0 / 1, {* stake_credential => delta_coin} / coin]`),
    //     sha256 e71cbf08f4fe62bb654f8371f4934eba750c17546b7d44c0c4f38b470eaabcd6
    // - Relay: Pallas daecef59a1979d89515524d9583cc28655af182c:test_data/alonzo2.block (`84001917704447F4A4CDF6`)
    //   and TestData/X8Sources/ledger/conway.cbor (sha256 af8e6ddbe10cf726dd56ffb41a92830bca723954c287413a4c1077774d5679b3).
    // - DRep: TestData/X7CliGoldens.json ("ledger-conway") and TestData/X8Sources/ledger/conway.cbor.
    // - ProtocolParamUpdate: TestData/X7CliGoldens.json ("ledger-conway"), TestData/X8Sources/ledger/conway.cbor,
    //   and TestData/X8Sources/ledger/babbage.cbor (sha256 df17f7472a16c1c03add8a396633499b34f4bb900da4eb4a42bb76de355801cc).

    [Fact]
    public void FirstFamilyUnionsHave24ByteStructLayoutAndZeroOverhead()
    {
        Assert.Equal(24, Unsafe.SizeOf<Nonce>());
        Assert.Equal(24, Unsafe.SizeOf<Target>());
        Assert.Equal(24, Unsafe.SizeOf<Relay>());
        Assert.Equal(24, Unsafe.SizeOf<DRep>());
        Assert.Equal(24, Unsafe.SizeOf<ProtocolParamUpdate>());
    }

    [Fact]
    public void DefaultUnionValuesAreInspectableAndNonSerializable()
    {
        CheckDefault<Nonce>("Nonce(default)", static u => u.HasValue, static u => u is null, static (w, u) => Nonce.Write(w, u),
            static u => u.TryGetValue(out NonceWithoutHash _) || u.TryGetValue(out NonceWithHash _));
        CheckDefault<Target>("Target(default)", static u => u.HasValue, static u => u is null, static (w, u) => Target.Write(w, u),
            static u => u.TryGetValue(out StakeCredentials _) || u.TryGetValue(out OtherAccountingPot _));
        CheckDefault<Relay>("Relay(default)", static u => u.HasValue, static u => u is null, static (w, u) => Relay.Write(w, u),
            static u => u.TryGetValue(out SingleHostAddr _) || u.TryGetValue(out SingleHostName _) || u.TryGetValue(out MultiHostName _));
        CheckDefault<DRep>("DRep(default)", static u => u.HasValue, static u => u is null, static (w, u) => DRep.Write(w, u),
            static u => u.TryGetValue(out DRepAddrKeyHash _) || u.TryGetValue(out DRepScriptHash _) || u.TryGetValue(out Abstain _) || u.TryGetValue(out DRepNoConfidence _));
        CheckDefault<ProtocolParamUpdate>("ProtocolParamUpdate(default)", static u => u.HasValue, static u => u is null, static (w, u) => ProtocolParamUpdate.Write(w, u),
            static u => u.TryGetValue(out ConwayProtocolParamUpdate _) || u.TryGetValue(out BabbageProtocolParamUpdate _));
    }

    private static void CheckDefault<T>(string expectedToString, Func<T, bool> hasValue, Func<T, bool> isNullPattern, Action<IBufferWriter<byte>, T> write, Func<T, bool> anyTryGet)
        where T : struct, ICborType, IUnion, IEquatable<T>
    {
        T def = default;
        Assert.False(hasValue(def));
        Assert.Null(def.Value);
        Assert.True(def.Raw.IsEmpty);
        Assert.False(anyTryGet(def));
        Assert.True(def.Equals(default));
        Assert.Equal(expectedToString, def.ToString());
        Assert.True(isNullPattern(def));
        Assert.Equal(-1, def.ConstrIndex);
        Assert.False(def.IsIndefinite);

        ArrayBufferWriter<byte> writer = new();
        _ = Assert.Throws<CborValidationException>(() => write(writer, def));
        _ = Assert.Throws<CborValidationException>(() => CborSerializer.Serialize(def));
        _ = Assert.Throws<CborValidationException>(() => CborSerializer.SerializeToMemory(def));
        _ = Assert.Throws<CborValidationException>(() => CborSerializer.GetEncodedLength(def));
    }

    [Fact]
    public void ConstructorsEnforceWireArityDiscriminantClassRawAndEraIntegrity()
    {
        // Prepared implicit conversion allocates 0 B; nullable union conversion works cleanly.
        Abstain preparedAbstain = Abstain.Create(2);
        DRep implicitConverted = default;
        long convBytes = AllocationAssert.Measure(
            () => implicitConverted = preparedAbstain,
            () => implicitConverted = preparedAbstain,
            () => Assert.True(implicitConverted is Abstain));
        Assert.Equal(0, convBytes);
        DRep? nullableDRep = preparedAbstain;
        Assert.True(nullableDRep.HasValue && nullableDRep.Value is Abstain);

        // Indefinite-array case accepted by public constructor.
        DRep indefDRep = new(Abstain.Read(Convert.FromHexString("9F02FF")));
        Assert.True(indefDRep.HasValue && indefDRep is Abstain);

        // Uninitialized required-field structs and wrong tags throw CborValidationException.
        _ = Assert.Throws<CborValidationException>(() => new DRep(default(Abstain)));
        _ = Assert.Throws<CborValidationException>(() => new Nonce(default(NonceWithoutHash)));
        _ = Assert.Throws<CborValidationException>(() => new Relay(default(SingleHostAddr)));
        _ = Assert.Throws<CborValidationException>(() => new Target(default(OtherAccountingPot)));
        _ = Assert.Throws<CborValidationException>(() => new DRep(Abstain.Create(3)));
        _ = Assert.Throws<CborValidationException>(() => new Nonce(NonceWithoutHash.Create(9)));
        _ = Assert.Throws<CborValidationException>(() => new Relay(MultiHostName.Create(0, "a.invalid")));

        // Truncated optional-before-required [1, null] SingleHostName throws CborValidationException (minItems = 3).
        SingleHostName truncatedHost = SingleHostName.Read(Convert.FromHexString("8201F6"));
        _ = Assert.Throws<CborValidationException>(() => new Relay(truncatedHost));

        // Synthetic CborRecord class case: null! -> empty default; tampered/stale Raw -> CborValidationException.
        SyntheticClassCaseUnion nullWrapped = new((CborClassCase)null!);
        Assert.False(nullWrapped.HasValue);
        Assert.Null(nullWrapped.Value);

        CborClassCase validClassCase = CborClassCase.Create(0);
        Assert.True(new SyntheticClassCaseUnion(validClassCase).HasValue);
        _ = Assert.Throws<CborValidationException>(() => new SyntheticClassCaseUnion(validClassCase with { Raw = Convert.FromHexString("8109") }));
        _ = Assert.Throws<CborValidationException>(() => new SyntheticClassCaseUnion(validClassCase with { Raw = Convert.FromHexString("816178") }));
        _ = Assert.Throws<CborValidationException>(() => new SyntheticClassCaseUnion(validClassCase with { Raw = Convert.FromHexString("81") }));
        _ = Assert.Throws<CborValidationException>(() => new SyntheticClassCaseUnion(validClassCase with { Raw = Convert.FromHexString("810000") }));

        OtherClassCase validMapClass = OtherClassCase.Create(1);
        _ = Assert.Throws<CborValidationException>(() => new SyntheticClassCaseUnion(validMapClass with { Raw = Convert.FromHexString("C1A0") }));
        _ = Assert.Throws<CborValidationException>(() => new SyntheticClassCaseUnion(validMapClass with { Raw = Convert.FromHexString("D90102A0") }));

        byte[] badMapValue = Convert.FromHexString("A1006178");
        _ = Assert.Throws<CborDecodeException>(() => SyntheticClassCaseUnion.Read(badMapValue));
        _ = Assert.Throws<CborValidationException>(() => new SyntheticClassCaseUnion(validMapClass with { Raw = badMapValue }));

        byte[] dupMapKeys = Convert.FromHexString("A200010002");
        _ = Assert.Throws<CborDecodeException>(() => SyntheticClassCaseUnion.Read(dupMapKeys));
        _ = Assert.Throws<CborValidationException>(() => new SyntheticClassCaseUnion(validMapClass with { Raw = dupMapKeys }));

        byte[] nonIntMapKey = Convert.FromHexString("A1616101");
        _ = Assert.Throws<CborDecodeException>(() => SyntheticClassCaseUnion.Read(nonIntMapKey));
        _ = Assert.Throws<CborValidationException>(() => new SyntheticClassCaseUnion(validMapClass with { Raw = nonIntMapKey }));

        byte[] validMapRaw = Convert.FromHexString("A10007");
        SyntheticClassCaseUnion validMapUnion = new(OtherClassCase.Read(validMapRaw));
        Assert.Equal(validMapRaw, CborSerializer.Serialize(validMapUnion));
        Assert.True(SyntheticClassCaseUnion.Read(validMapRaw) is OtherClassCase { Value: 7 });

        // ProtocolParamUpdate era-key constructor guards.
        BabbageProtocolParamUpdate conwayKeyInBabbage = BabbageProtocolParamUpdate.Read(Convert.FromHexString("A1181B01"));
        _ = Assert.Throws<CborValidationException>(() => new ProtocolParamUpdate(conwayKeyInBabbage));

        ConwayProtocolParamUpdate babbageIntInConway = ConwayProtocolParamUpdate.Read(Convert.FromHexString("A10E08"));
        _ = Assert.Throws<CborValidationException>(() => new ProtocolParamUpdate(babbageIntInConway));
        ConwayProtocolParamUpdate babbageArrInConway = ConwayProtocolParamUpdate.Read(Convert.FromHexString("A10E820800"));
        _ = Assert.Throws<CborValidationException>(() => new ProtocolParamUpdate(babbageArrInConway));

        byte[] mixedBytes = Convert.FromHexString("A20E820800181901");
        _ = Assert.Throws<CborValidationException>(() => new ProtocolParamUpdate(BabbageProtocolParamUpdate.Read(mixedBytes)));
        _ = Assert.Throws<CborValidationException>(() => new ProtocolParamUpdate(ConwayProtocolParamUpdate.Read(mixedBytes)));

        // Common-only maps allowed in either ctor (preserving CLR variant), while Read deterministically chooses Conway.
        byte[] emptyMap = Convert.FromHexString("A0");
        byte[] commonKeyZero = Convert.FromHexString("A100182C");
        ProtocolParamUpdate bEmpty = new(BabbageProtocolParamUpdate.Read(emptyMap));
        ProtocolParamUpdate cEmpty = new(ConwayProtocolParamUpdate.Read(emptyMap));
        ProtocolParamUpdate bCommon = new(BabbageProtocolParamUpdate.Read(commonKeyZero));
        ProtocolParamUpdate cCommon = new(ConwayProtocolParamUpdate.Read(commonKeyZero));
        Assert.True(bEmpty is BabbageProtocolParamUpdate);
        Assert.True(cEmpty is ConwayProtocolParamUpdate);
        Assert.True(bCommon is BabbageProtocolParamUpdate);
        Assert.True(cCommon is ConwayProtocolParamUpdate);
        Assert.False(bEmpty.Equals(cEmpty));
        Assert.True(ProtocolParamUpdate.Read(emptyMap) is ConwayProtocolParamUpdate);
        Assert.True(ProtocolParamUpdate.Read(commonKeyZero) is ConwayProtocolParamUpdate);

        // Positive CborUnionWire.EncodeFresh path: valid empty-Raw struct case encodes fresh and round-trips.
        ProtocolParamUpdate freshDefaultConway = new(default(ConwayProtocolParamUpdate));
        Assert.Equal(emptyMap, freshDefaultConway.Raw.ToArray());
        Assert.True(ProtocolParamUpdate.Read(CborSerializer.Serialize(freshDefaultConway)) is ConwayProtocolParamUpdate);

        // Babbage-only keys 12..15 round-trip as BabbageProtocolParamUpdate.
        foreach (string hex in new[] { "A10C00", "A10D00", "A10E00", "A10F00" })
        {
            byte[] raw = Convert.FromHexString(hex);
            ProtocolParamUpdate ppu = ProtocolParamUpdate.Read(raw);
            Assert.True(ppu is BabbageProtocolParamUpdate);
            Assert.Equal(raw, CborSerializer.Serialize(ppu));
        }
    }

    [Fact]
    public void DirectReadIngressRejectsHostileMalformedTruncatedAndZeroProgressInputs()
    {
        // Empty input and rejected roots across all 5 unions.
        foreach (string hex in new[] { "", "80", "F6", "F7", "FF" })
        {
            byte[] bytes = Convert.FromHexString(hex);
            _ = Assert.Throws<CborDecodeException>(() => Nonce.Read(bytes));
            _ = Assert.Throws<CborDecodeException>(() => Target.Read(bytes));
            _ = Assert.Throws<CborDecodeException>(() => Relay.Read(bytes));
            _ = Assert.Throws<CborDecodeException>(() => DRep.Read(bytes));
            _ = Assert.Throws<CborDecodeException>(() => ProtocolParamUpdate.Read(bytes));
        }

        // Truncated and wrong-shape payloads on direct Nonce.Read.
        foreach (string hex in new[] { "81", "8201582001", "9F00", "81FF", "8101", "820005" })
        {
            _ = Assert.Throws<CborDecodeException>(() => Nonce.Read(Convert.FromHexString(hex)));
        }

        // Direct [CborList] short-array read ingress (ValidateReadListSlice).
        _ = Assert.Throws<CborDecodeException>(() => Nonce.Read(Convert.FromHexString("9F01FF")));
        _ = Assert.Throws<CborDecodeException>(() => DRep.Read(Convert.FromHexString("9F00FF")));
        _ = Assert.Throws<CborDecodeException>(() => Relay.Read(Convert.FromHexString("8201F6")));
        _ = Assert.Throws<CborDecodeException>(() => Relay.Read(Convert.FromHexString("9F01F6FF")));

        // ProtocolParamUpdate hostile map inputs (missing 0xFF break and stray 0xFF values).
        foreach (string hex in new[] { "BF0001", "A100FF", "BF00FFFF", "A1FF" })
        {
            _ = Assert.Throws<CborDecodeException>(() => ProtocolParamUpdate.Read(Convert.FromHexString(hex)));
        }

        // Streaming trailing-item preservation across all 5 unions.
        byte[] twoNonces = Convert.FromHexString("81008100");
        _ = Nonce.Read(twoNonces, out int nConsumed);
        Assert.Equal(2, nConsumed);

        byte[] twoTargets = Convert.FromHexString("0506");
        _ = Target.Read(twoTargets, out int tConsumed);
        Assert.Equal(1, tConsumed);

        byte[] twoRelays = Convert.FromHexString("8202616182026162");
        _ = Relay.Read(twoRelays, out int rConsumed);
        Assert.Equal(4, rConsumed);

        byte[] twoDReps = Convert.FromHexString("81028103");
        _ = DRep.Read(twoDReps, out int dConsumed);
        Assert.Equal(2, dConsumed);

        byte[] twoUpdates = Convert.FromHexString("A10001A10002");
        _ = ProtocolParamUpdate.Read(twoUpdates, out int pConsumed);
        Assert.Equal(3, pConsumed);

        // Zero-progress external manual reader rejected with CborDecodeException.
        _ = Assert.Throws<CborDecodeException>(() => ZeroProgressManualUnion.Read((byte[])[0x01]));

        // 0xF6 and 0xF7 permitted as nullable fields inside SingleHostAddr.
        foreach (string hex in new[] { "8400F6F6F6", "8400F7F7F7" })
        {
            Relay relay = Relay.Read(Convert.FromHexString(hex));
            Assert.True(relay.TryGetValue(out SingleHostAddr addr));
            Assert.Null(addr.Port);
            Assert.Null(addr.Ipv4);
            Assert.Null(addr.Ipv6);
        }
    }

    [Fact]
    public void FirstFamilyReadSwitchConstructAndListIterationAllocateZeroBytes()
    {
        byte[] nonceWire = Convert.FromHexString("82015820" + new string('1', 64));
        byte[] targetWire = Convert.FromHexString("1A004C4B40");
        byte[] relayWire = Convert.FromHexString("8400190BB9447F000001F6");
        byte[] drepWire = Convert.FromHexString("8102");
        byte[] ppuWire = Convert.FromHexString("A1181B01");

        NonceWithHash nonceCase = NonceWithHash.Read(nonceWire);
        OtherAccountingPot targetCase = OtherAccountingPot.Read(targetWire);
        SingleHostAddr relayCase = SingleHostAddr.Read(relayWire);
        Abstain drepCase = Abstain.Read(drepWire);
        ConwayProtocolParamUpdate ppuCase = ConwayProtocolParamUpdate.Read(ppuWire);

        Nonce nonceVal = nonceCase;
        Target targetVal = targetCase;
        Relay relayVal = relayCase;
        DRep drepVal = drepCase;
        ProtocolParamUpdate ppuVal = ppuCase;

        byte[] poolBytes = EncodePoolWithRelays(relayWire);
        PoolRegistration pool = PoolRegistration.Read(poolBytes);
        _ = pool.Relays; // Warm CborCodecCache.GetReader<Relay>() outside measured block.

        int sink = 0;

        static int SwitchNonce(Nonce n) => n switch
        {
            NonceWithHash h => h.Tag + h.Hash.Length,
            NonceWithoutHash w => w.Tag,
            null => -1
        };
        static int SwitchTarget(Target t) => t switch
        {
            StakeCredentials s => s.Raw.Length,
            OtherAccountingPot o => (int)o.Amount,
            null => -1
        };
        static int SwitchRelay(Relay r) => r switch
        {
            SingleHostAddr a => a.Tag + (a.Port ?? 0),
            SingleHostName s => s.Tag,
            MultiHostName m => m.Tag,
            null => -1
        };
        static int SwitchDRep(DRep d) => d switch
        {
            DRepAddrKeyHash k => k.Tag,
            DRepScriptHash s => s.Tag,
            Abstain a => a.Tag,
            DRepNoConfidence c => c.Tag,
            null => -1
        };
        static int SwitchPpu(ProtocolParamUpdate p) => p switch
        {
            ConwayProtocolParamUpdate c => c.Raw.Length,
            BabbageProtocolParamUpdate b => b.Raw.Length,
            null => -1
        };

        // Read = 0 B for all 5.
        Assert.Equal(0, AllocationAssert.Measure(() => sink = Nonce.Read(nonceWire).Raw.Length, () => sink = Nonce.Read(nonceWire).Raw.Length));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = Target.Read(targetWire).Raw.Length, () => sink = Target.Read(targetWire).Raw.Length));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = Relay.Read(relayWire).Raw.Length, () => sink = Relay.Read(relayWire).Raw.Length));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = DRep.Read(drepWire).Raw.Length, () => sink = DRep.Read(drepWire).Raw.Length));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = ProtocolParamUpdate.Read(ppuWire).Raw.Length, () => sink = ProtocolParamUpdate.Read(ppuWire).Raw.Length));

        // C# 15 compiler-lowered switch = 0 B for all 5.
        Assert.Equal(0, AllocationAssert.Measure(() => sink = SwitchNonce(nonceVal), () => sink = SwitchNonce(nonceVal)));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = SwitchTarget(targetVal), () => sink = SwitchTarget(targetVal)));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = SwitchRelay(relayVal), () => sink = SwitchRelay(relayVal)));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = SwitchDRep(drepVal), () => sink = SwitchDRep(drepVal)));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = SwitchPpu(ppuVal), () => sink = SwitchPpu(ppuVal)));

        // Exhaustive switch statements on default(Union) and all 4 DRep variants + extensions = 0 B.
        byte[] drepHash = new byte[28];
        DRep drepKey = DRepAddrKeyHash.Create(0, drepHash);
        DRep drepScript = DRepScriptHash.Create(1, drepHash);
        DRep drepNoConf = DRepNoConfidence.Create(3);
        Assert.Equal(-1, SwitchNonce(default));
        Assert.Equal(-1, SwitchTarget(default));
        Assert.Equal(-1, SwitchRelay(default));
        Assert.Equal(-1, SwitchDRep(default));
        Assert.Equal(-1, SwitchPpu(default));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = SwitchNonce(default), () => sink = SwitchNonce(default)));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = SwitchTarget(default), () => sink = SwitchTarget(default)));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = SwitchRelay(default), () => sink = SwitchRelay(default)));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = SwitchDRep(default), () => sink = SwitchDRep(default)));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = SwitchPpu(default), () => sink = SwitchPpu(default)));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = SwitchDRep(drepKey) + SwitchDRep(drepScript) + SwitchDRep(drepNoConf),
            () => sink = SwitchDRep(drepKey) + SwitchDRep(drepScript) + SwitchDRep(drepNoConf)));
        Assert.Equal(0, AllocationAssert.Measure(() => sink = drepKey.Tag() + drepScript.Tag() + drepVal.Tag() + drepNoConf.Tag(),
            () => sink = drepKey.Tag() + drepScript.Tag() + drepVal.Tag() + drepNoConf.Tag()));
        Assert.Equal(0, AllocationAssert.Measure(
            () => sink = (drepKey.KeyHash()?.Length ?? 0) + (drepScript.KeyHash()?.Length ?? 0) + (drepVal.KeyHash()?.Length ?? 0) + (drepNoConf.KeyHash()?.Length ?? 0) + (default(DRep).KeyHash()?.Length ?? 0),
            () => sink = (drepKey.KeyHash()?.Length ?? 0) + (drepScript.KeyHash()?.Length ?? 0) + (drepVal.KeyHash()?.Length ?? 0) + (drepNoConf.KeyHash()?.Length ?? 0) + (default(DRep).KeyHash()?.Length ?? 0)));

        // TryGetValue = 0 B for all 5.
        Assert.Equal(0, AllocationAssert.Measure(
            () => sink = nonceVal.TryGetValue(out NonceWithHash nh) ? nh.Tag : 0,
            () => sink = nonceVal.TryGetValue(out NonceWithHash nh) ? nh.Tag : 0));
        Assert.Equal(0, AllocationAssert.Measure(
            () => sink = targetVal.TryGetValue(out OtherAccountingPot op) ? (int)op.Amount : 0,
            () => sink = targetVal.TryGetValue(out OtherAccountingPot op) ? (int)op.Amount : 0));
        Assert.Equal(0, AllocationAssert.Measure(
            () => sink = relayVal.TryGetValue(out SingleHostAddr sa) ? sa.Tag : 0,
            () => sink = relayVal.TryGetValue(out SingleHostAddr sa) ? sa.Tag : 0));
        Assert.Equal(0, AllocationAssert.Measure(
            () => sink = drepVal.TryGetValue(out Abstain ab) ? ab.Tag : 0,
            () => sink = drepVal.TryGetValue(out Abstain ab) ? ab.Tag : 0));
        Assert.Equal(0, AllocationAssert.Measure(
            () => sink = ppuVal.TryGetValue(out ConwayProtocolParamUpdate cp) ? cp.Raw.Length : 0,
            () => sink = ppuVal.TryGetValue(out ConwayProtocolParamUpdate cp) ? cp.Raw.Length : 0));

        // Prepared-case implicit conversion / constructor = 0 B for all 5.
        Assert.Equal(0, AllocationAssert.Measure(() => { Nonce x = nonceCase; sink = x.Raw.Length; }, () => { Nonce x = nonceCase; sink = x.Raw.Length; }));
        Assert.Equal(0, AllocationAssert.Measure(() => { Target x = targetCase; sink = x.Raw.Length; }, () => { Target x = targetCase; sink = x.Raw.Length; }));
        Assert.Equal(0, AllocationAssert.Measure(() => { Relay x = relayCase; sink = x.Raw.Length; }, () => { Relay x = relayCase; sink = x.Raw.Length; }));
        Assert.Equal(0, AllocationAssert.Measure(() => { DRep x = drepCase; sink = x.Raw.Length; }, () => { DRep x = drepCase; sink = x.Raw.Length; }));
        Assert.Equal(0, AllocationAssert.Measure(() => { ProtocolParamUpdate x = ppuCase; sink = x.Raw.Length; }, () => { ProtocolParamUpdate x = ppuCase; sink = x.Raw.Length; }));

        // CborList<Relay> foreach + switch = 0 B.
        Assert.Equal(0, AllocationAssert.Measure(
            () => { int acc = 0; foreach (Relay r in pool.Relays) { acc += SwitchRelay(r); } sink = acc; },
            () => { int acc = 0; foreach (Relay r in pool.Relays) { acc += SwitchRelay(r); } sink = acc; }));

        // Positive control: .Value boxes the struct case (> 0 B).
        object? boxed = null;
        long boxedBytes = AllocationAssert.Measure(() => boxed = drepVal.Value, () => boxed = drepVal.Value);
        Assert.True(boxedBytes >= 24, $"Expected .Value boxing >= 24 B, got {boxedBytes}.");
        Assert.NotNull(boxed);
        _ = sink;
    }

    private static byte[] EncodePoolWithRelays(byte[] relayItem)
    {
        ArrayBufferWriter<byte> buf = new();
        SAIB.Cbor.Serialization.CborWriter w = new(buf);
        w.WriteBeginArray(10);
        w.WriteInt32(3);
        w.WriteByteString(new byte[28]);
        w.WriteByteString(new byte[32]);
        w.WriteUInt64(0);
        w.WriteUInt64(0);
        w.WriteSemanticTag(30);
        w.WriteBeginArray(2);
        w.WriteUInt64(1);
        w.WriteUInt64(100);
        w.WriteByteString(new byte[29]);
        w.WriteBeginArray(0);
        w.WriteBeginArray(1);
        buf.Write(relayItem);
        w.WriteNull();
        return buf.WrittenSpan.ToArray();
    }

    [Fact]
    public void IndependentOracleFreshRebuildMatchesWireBytesAcrossAllFiveUnions()
    {
        // 1. Nonce (allegra.cddl:262).
        byte[] nonce0 = Convert.FromHexString("8100");
        byte[] nonce1 = Convert.FromHexString("82015820" + new string('A', 64));
        Nonce n0 = Nonce.Read(nonce0);
        Nonce n1 = Nonce.Read(nonce1);
        Assert.True(n0.TryGetValue(out NonceWithoutHash n0Case));
        Assert.True(n1.TryGetValue(out NonceWithHash n1Case));
        Assert.Equal(nonce0, CborSerializer.Serialize(new Nonce(NonceWithoutHash.Create(n0Case.Tag))));
        Assert.Equal(nonce1, CborSerializer.Serialize(new Nonce(NonceWithHash.Create(n1Case.Tag, n1Case.Hash))));

        // 2. Target via MoveInstantaneousReward (allegra.cddl:223, incl. negative delta_coin).
        byte[] mirPotWire = Convert.FromHexString("82001A000F4240");
        MoveInstantaneousReward mirPot = MoveInstantaneousReward.Read(mirPotWire);
        Assert.True(mirPot.Target.TryGetValue(out OtherAccountingPot potCase));
        Assert.Equal(mirPotWire, CborSerializer.Serialize(MoveInstantaneousReward.Create(
            mirPot.Source, new Target(OtherAccountingPot.Create(potCase.Amount)))));

        byte[] mirCredsWire = Convert.FromHexString("8201A28200581C" + new string('1', 56) + "1903E88201581C" + new string('2', 56) + "3863");
        MoveInstantaneousReward mirCreds = MoveInstantaneousReward.Read(mirCredsWire);
        Assert.True(mirCreds.Target.TryGetValue(out StakeCredentials creds));
        Assert.Equal(mirCredsWire, CborSerializer.Serialize(MoveInstantaneousReward.Create(
            mirCreds.Source, new Target(StakeCredentials.Create(creds.Value)))));

        // 3. Relay (Pallas alonzo2.block + 4-field non-null IPv6 SingleHostAddr + ledger/conway.cbor).
        byte[] alonzoRelay = Convert.FromHexString("84001917704447F4A4CDF6");
        Assert.True(Relay.Read(alonzoRelay).TryGetValue(out SingleHostAddr rAddr));
        Assert.Equal(0, rAddr.Tag);
        Assert.Equal(6000, rAddr.Port);
        Assert.Equal([0x47, 0xF4, 0xA4, 0xCD], rAddr.Ipv4!.Value.ToArray());
        Assert.Null(rAddr.Ipv6);
        byte[] rebuiltAlonzo = CborSerializer.Serialize(new Relay(SingleHostAddr.Create(rAddr.Tag, rAddr.Port, rAddr.Ipv4, rAddr.Ipv6)));
        Assert.True(Relay.Read(rebuiltAlonzo).TryGetValue(out SingleHostAddr rebuiltAddr));
        Assert.Equal(rAddr.Ipv4!.Value.ToArray(), rebuiltAddr.Ipv4!.Value.ToArray());

        byte[] ipv6Relay = Convert.FromHexString("8400190BB9447F00000150" + new string('1', 32));
        Assert.True(Relay.Read(ipv6Relay).TryGetValue(out SingleHostAddr fullAddr));
        Assert.Equal(ipv6Relay, CborSerializer.Serialize(new Relay(SingleHostAddr.Create(fullAddr.Tag, fullAddr.Port, fullAddr.Ipv4, fullAddr.Ipv6))));

        // 4. DRep & ProtocolParamUpdate from ledger/conway.cbor, ledger/babbage.cbor, and X7CliGoldens.json.
        byte[] conwayTxBytes = VerifiedOracle("TestData/X8Sources/ledger/conway.cbor", "af8e6ddbe10cf726dd56ffb41a92830bca723954c287413a4c1077774d5679b3");
        PostMaryTransaction conwayTx = (PostMaryTransaction)ITransaction.Read(conwayTxBytes, Era.Conway);
        ConwayTransactionBody conwayBody = (ConwayTransactionBody)conwayTx.Body;
        int drepCount = 0;
        int relayCount = 0;
        int proposalCount = 0;
        if (conwayBody.Certificates is { } certs)
        {
            foreach (ICertificate cert in certs)
            {
                if (cert.DRep() is { } drep)
                {
                    drepCount++;
                    DRep rebuilt = drep switch
                    {
                        DRepAddrKeyHash k => DRepAddrKeyHash.Create(k.Tag, k.KeyHash),
                        DRepScriptHash s => DRepScriptHash.Create(s.Tag, s.ScriptHash),
                        Abstain a => Abstain.Create(a.Tag),
                        DRepNoConfidence nc => DRepNoConfidence.Create(nc.Tag),
                        null => throw new InvalidOperationException()
                    };
                    Assert.Equal(drep.Raw.ToArray(), CborSerializer.Serialize(rebuilt));
                }
                if (cert is PoolRegistration reg)
                {
                    foreach (Relay relay in reg.Relays)
                    {
                        relayCount++;
                        Relay rebuilt = relay switch
                        {
                            SingleHostAddr a => SingleHostAddr.Create(a.Tag, a.Port, a.Ipv4, a.Ipv6),
                            SingleHostName s => SingleHostName.Create(s.Tag, s.DnsName, s.Port),
                            MultiHostName m => MultiHostName.Create(m.Tag, m.DnsName),
                            null => throw new InvalidOperationException()
                        };
                        Assert.Equal(relay.Raw.ToArray(), CborSerializer.Serialize(rebuilt));
                    }
                }
            }
        }
        if (conwayBody.ProposalProcedures is { } proposals)
        {
            foreach (ProposalProcedure prop in proposals)
            {
                if (prop.GovAction is ParameterChangeAction pca)
                {
                    proposalCount++;
                    ProtocolParamUpdate rebuilt = pca.ProtocolParamUpdate switch
                    {
                        ConwayProtocolParamUpdate c => ConwayProtocolParamUpdate.Create(
                            c.MinFeeA, c.MinFeeB, c.MaxBlockBodySize, c.MaxTransactionSize, c.MaxBlockHeaderSize,
                            c.KeyDeposit, c.PoolDeposit, c.MaxEpoch, c.NOpt, c.PoolPledgeInfluence, c.ExpansionRate,
                            c.TreasuryGrowthRate, c.MinPoolCost, c.AdaPerUtxoByte, c.CostModels, c.ExecutionCosts,
                            c.MaxTxExUnits, c.MaxBlockExUnits, c.MaxValueSize, c.CollateralPercentage, c.MaxCollateralInputs,
                            c.PoolVotingThresholds, c.DRepVotingThresholds, c.MinCommitteeSize, c.CommitteeTermLimit,
                            c.GovActionLifetime, c.GovActionDeposit, c.DRepDeposit, c.DRepInactivityPeriod,
                            c.MinFeeRefScriptCostPerByte),
                        BabbageProtocolParamUpdate b => BabbageProtocolParamUpdate.Create(
                            b.MinFeeA, b.MinFeeB, b.MaxBlockBodySize, b.MaxTransactionSize, b.MaxBlockHeaderSize,
                            b.KeyDeposit, b.PoolDeposit, b.MaxEpoch, b.NOpt, b.PoolPledgeInfluence, b.ExpansionRate,
                            b.TreasuryGrowthRate, b.ProtocolVersionMajor, b.ProtocolVersionMinor, b.MinPoolCost,
                            b.AdaPerUtxoByte, b.CostModels, b.ExecutionCosts, b.MaxTxExUnits, b.MaxBlockExUnits,
                            b.MaxValueSize, b.CollateralPercentage, b.MaxCollateralInputs),
                        null => throw new InvalidOperationException()
                    };
                    Assert.Equal(pca.ProtocolParamUpdate.Raw.ToArray(), CborSerializer.Serialize(rebuilt));
                }
            }
        }
        Assert.True(drepCount > 0);
        Assert.True(relayCount > 0);
        Assert.True(proposalCount > 0);

        int babbagePpuCount = 0;
        byte[] babbageTxBytes = VerifiedOracle("TestData/X8Sources/ledger/babbage.cbor", "df17f7472a16c1c03add8a396633499b34f4bb900da4eb4a42bb76de355801cc");
        PostMaryTransaction babbageTx = (PostMaryTransaction)ITransaction.Read(babbageTxBytes, Era.Babbage);
        if (((BabbageTransactionBody)babbageTx.Body).Update is { } update)
        {
            foreach (ProtocolParamUpdate ppu in update.ProposedProtocolParameterUpdates.Value.Values)
            {
                babbagePpuCount++;
                Assert.True(ppu.HasValue);
                Assert.Equal(ppu.Raw.ToArray(), CborSerializer.Serialize(ppu));
            }
        }
        Assert.True(babbagePpuCount > 0);

        using JsonDocument goldenDoc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "X7CliGoldens.json")));
        JsonElement cliConway = goldenDoc.RootElement.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("id").GetString() == "ledger-conway");
        byte[] cliTxBytes = Convert.FromHexString(cliConway.GetProperty("cborHex").GetString()!);
        PostMaryTransaction cliTx = (PostMaryTransaction)ITransaction.Read(cliTxBytes, Era.Conway);
        Assert.NotNull(cliTx.Body);
    }

    private static byte[] VerifiedOracle(string relativePath, string expectedSha256)
    {
        string root = RepositoryRoot();
        byte[] bytes = File.ReadAllBytes(Path.Combine(root, "src", "Chrysalis.Codec.Test", relativePath));
        Assert.Equal(expectedSha256, Convert.ToHexStringLower(SHA256.HashData(bytes)));
        return bytes;
    }

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? path = new(AppContext.BaseDirectory); path is not null; path = path.Parent)
        {
            if (File.Exists(Path.Combine(path.FullName, "Chrysalis.slnx")))
            {
                return path.FullName;
            }
        }
        throw new DirectoryNotFoundException("Cannot locate Chrysalis fixture sources from the test assembly.");
    }

    [Fact]
    public void EveryOfficialUnionInCodecTriggersCS8509WhenAnyCaseOmitted()
    {
        Assembly codecAsm = typeof(CborSerializer).Assembly;
        Type[] officialUnions =
        [
            .. codecAsm.GetTypes()
                .Where(static t => t.IsValueType && t.GetCustomAttributesData()
                    .Any(static a => a.AttributeType.FullName == "System.Runtime.CompilerServices.UnionAttribute"))
                .OrderBy(static t => t.Name, StringComparer.Ordinal)
        ];
        Assert.Equal(["DRep", "Nonce", "ProtocolParamUpdate", "Relay", "Target"], [.. officialUnions.Select(static t => t.Name)]);

        string platformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string
            ?? throw new InvalidOperationException("Missing TRUSTED_PLATFORM_ASSEMBLIES.");
        MetadataReference[] refs =
        [
            .. platformAssemblies.Split(Path.PathSeparator)
                .Append(codecAsm.Location)
                .Distinct(StringComparer.Ordinal)
                .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
        ];
        CSharpParseOptions parseOptions = new(LanguageVersion.Latest);

        foreach (Type unionType in officialUnions)
        {
            string[] caseNames =
            [
                .. unionType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(static m => m.Name == "TryGetValue" && m.ReturnType == typeof(bool) && m.GetParameters() is [ParameterInfo p] && p.IsOut)
                    .Select(static m => m.GetParameters()[0].ParameterType.GetElementType()!.FullName!)
                    .OrderBy(static n => n, StringComparer.Ordinal)
            ];
            Assert.NotEmpty(caseNames);

            string BuildSwitch(IEnumerable<string> includedCases, bool includeNull)
            {
                string arms = string.Join("\n            ", includedCases.Select(static (c, i) => $"{c} => {i},"));
                if (includeNull)
                {
                    arms += "\n            null => -1";
                }
                return $$"""
                    #nullable enable
                    using System;
                    namespace SwitchAudit;
                    public static class Probe
                    {
                        public static int Classify({{unionType.FullName}} value) => value switch
                        {
                            {{arms}}
                        };
                    }
                    """;
            }

            // Complete switch (all cases + null) produces 0 CS8509 diagnostics.
            CSharpCompilation fullComp = CSharpCompilation.Create("FullSwitch_" + unionType.Name,
                [CSharpSyntaxTree.ParseText(BuildSwitch(caseNames, includeNull: true), parseOptions)],
                refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Diagnostic[] fullExhaustive = [.. fullComp.GetDiagnostics().Where(static d => d.Id == "CS8509")];
            Assert.Empty(fullExhaustive);

            // Omitting each case one by one triggers CS8509 naming the omitted case.
            foreach (string omitted in caseNames)
            {
                CSharpCompilation partialComp = CSharpCompilation.Create("MissingSwitch_" + unionType.Name,
                    [CSharpSyntaxTree.ParseText(BuildSwitch(caseNames.Where(c => c != omitted), includeNull: true), parseOptions)],
                    refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                Diagnostic diag = Assert.Single(partialComp.GetDiagnostics(), static d => d.Id == "CS8509");
                string shortName = omitted[(omitted.LastIndexOf('.', StringComparison.Ordinal) + 1)..];
                Assert.Contains(shortName, diag.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
            }


        }
    }

    [Fact]
    public void ExtensionsHandleAllCasesAndDefaultContract()
    {
        byte[] hash = new byte[28];
        hash[0] = 0xAA;
        DRep addr = DRepAddrKeyHash.Create(0, hash);
        DRep script = DRepScriptHash.Create(1, hash);
        DRep abstain = Abstain.Create(2);
        DRep noConf = DRepNoConfidence.Create(3);
        DRep empty = default;

        Assert.Equal(0, addr.Tag());
        Assert.Equal(1, script.Tag());
        Assert.Equal(2, abstain.Tag());
        Assert.Equal(3, noConf.Tag());
        _ = Assert.Throws<CborValidationException>(() => empty.Tag());

        Assert.Equal(hash, addr.KeyHash()!.Value.ToArray());
        Assert.Equal(hash, script.KeyHash()!.Value.ToArray());
        Assert.Null(abstain.KeyHash());
        Assert.Null(noConf.KeyHash());
        Assert.Null(empty.KeyHash());

        Credential cred = Credential.Create(0, hash);
        VoteDelegCert voteCert = VoteDelegCert.Create(9, cred, abstain);
        StakeVoteDelegCert svCert = StakeVoteDelegCert.Create(10, cred, hash, addr);
        VoteRegDelegCert vrCert = VoteRegDelegCert.Create(12, cred, script, 500);
        StakeVoteRegDelegCert svrCert = StakeVoteRegDelegCert.Create(13, cred, hash, noConf, 500);
        StakeRegistration nonVoteCert = StakeRegistration.Create(0, cred);

        Assert.True(voteCert.DRep() is Abstain);
        Assert.True(svCert.DRep() is DRepAddrKeyHash);
        Assert.True(vrCert.DRep() is DRepScriptHash);
        Assert.True(svrCert.DRep() is DRepNoConfidence);
        Assert.Null(nonVoteCert.DRep());
    }
}
