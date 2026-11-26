using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Chrysalis.Crypto;
using Chrysalis.Plutus.Builtins;
using Chrysalis.Plutus.Cek;
using Chrysalis.Plutus.Flat;
using Chrysalis.Plutus.Types;
using Chrysalis.Plutus.VM.EvalTx;
using Chrysalis.Plutus.VM.Models;
using Xunit;

namespace Chrysalis.Plutus.Test;

public sealed class Ed25519BuiltinTests
{
    // Test-only RFC 8032 §7.1 TEST 1 and TEST SHA(abc) vectors, plus public Curve25519 torsion/base-point constants.
    private const string Rfc8032Test1SeedHex = "9d61b19deffd5a60ba844af492ec2cc44449c5697b326919703bac031cae7f60";
    private const string Rfc8032Test1PublicKeyHex = "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a";
    private const string Rfc8032Test1SignatureHex = "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901555fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b";

    private const string Rfc8032ShaAbcPublicKeyHex = "ec172b93ad5e563bf4932c70e1245034c35467ef2efd4d64ebf819683467e2bf";
    private const string Rfc8032ShaAbcMessageHex = "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f";
    private const string Rfc8032ShaAbcSignatureHex = "dc2a4459e7369633a52b1bf277839a00201009a3efbf3ecb69bea2186c26b58909351fc9ac90b3ecfdfbc7c66431e0303dca179c138ac17ad9bef1177331a704";

    private const string BasePointHex = "5866666666666666666666666666666666666666666666666666666666666666";
    private const string IdentityHex = "0100000000000000000000000000000000000000000000000000000000000000";
    private const string Order2Hex = "ecffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff7f";
    private const string Order4PosHex = "0000000000000000000000000000000000000000000000000000000000000000";
    private const string Order8AHex = "c7176a703d4dd84fba3c0b760d10670f2a2053fa2c39ccc64ec7fd7792ac037a";

    private const string PublicKeyLengthError = "verifyEd25519Signature: public key must be 32 bytes";
    private const string SignatureLengthError = "verifyEd25519Signature: signature must be 64 bytes";

    private static readonly BigInteger SubgroupOrderL = (BigInteger.One << 252) +
        BigInteger.Parse("27742317777372353535851937790883648493", CultureInfo.InvariantCulture);

    private static readonly byte[] SampleLengthCheckMessage = [1, 2, 3];

    private sealed record WycheproofCase(int Id, string Result, string[] Flags, byte[] Signature, byte[] Message, byte[] PublicKey);

    [Theory]
    [InlineData("identity", IdentityHex)]
    [InlineData("order-2", Order2Hex)]
    [InlineData("order-4-pos", Order4PosHex)]
    [InlineData("order-8-A", Order8AHex)]
    public void VerifyEd25519_SmallOrderR_ReturnsFalse(string caseName, string rHex)
    {
        _ = caseName;
        byte[] pk = Convert.FromHexString(Rfc8032Test1PublicKeyHex);
        byte[] sig = CreateSmallOrderRSig(rHex, pk, Convert.FromHexString(Rfc8032Test1SeedHex));

        Assert.False(Ed25519.Verify(sig, [], pk));
        Assert.False(VerifyBuiltin(pk, [], sig));
    }

    [Fact]
    public void VerifyEd25519_MixedOrderPublicKey_CofactoredOnlyForgery_ReturnsFalse()
    {
        (byte[] pkMixed, byte[] msg, byte[] sig) = CreateMixedOrderForgery();

        Assert.False(Ed25519.Verify(sig, msg, pkMixed));
        Assert.False(VerifyBuiltin(pkMixed, msg, sig));
    }

    [Fact]
    public void Evaluator_VerifyEd25519Signature_EndToEnd_MatchesNodeSemantics()
    {
        byte[] validPk = Convert.FromHexString(Rfc8032Test1PublicKeyHex);
        byte[] validSig = Convert.FromHexString(Rfc8032Test1SignatureHex);
        Term<DeBruijn> validTerm = BuildVerifyTerm(validPk, [], validSig);

        (Term<DeBruijn> validEval, ExUnitsResult validUnits) = Evaluator.EvaluateTerm(validTerm);
        Assert.Equal(BoolConstant.True, Assert.IsType<ConstTerm<DeBruijn>>(validEval).Value);
        Assert.True(validUnits.Steps > 0);

        EvaluationResult validScriptResult = Evaluator.EvaluateScript(
            EncodeBranchScript(validTerm, expectTrue: true),
            Array.Empty<PlutusData>(),
            3,
            11);
        Assert.True(validScriptResult.ExUnits.Steps > 0);

        byte[] smallRSig = CreateSmallOrderRSig(IdentityHex, validPk, Convert.FromHexString(Rfc8032Test1SeedHex));
        (byte[] mixedPk, byte[] mixedMsg, byte[] mixedSig) = CreateMixedOrderForgery();
        (byte[] Pk, byte[] Msg, byte[] Sig)[] negativeCases =
        [
            (validPk, [], smallRSig),
            (mixedPk, mixedMsg, mixedSig),
        ];

        foreach ((byte[] pk, byte[] msg, byte[] sig) in negativeCases)
        {
            Term<DeBruijn> term = BuildVerifyTerm(pk, msg, sig);
            (Term<DeBruijn> eval, ExUnitsResult units) = Evaluator.EvaluateTerm(term);
            Assert.Equal(BoolConstant.False, Assert.IsType<ConstTerm<DeBruijn>>(eval).Value);
            Assert.True(units.Steps > 0);

            EvaluationResult falseScriptResult = Evaluator.EvaluateScript(
                EncodeBranchScript(term, expectTrue: false),
                Array.Empty<PlutusData>(),
                3,
                11);
            Assert.True(falseScriptResult.ExUnits.Steps > 0);

            _ = Assert.Throws<EvaluationException>(() => Evaluator.EvaluateScript(
                EncodeBranchScript(term, expectTrue: true),
                Array.Empty<PlutusData>(),
                3,
                11));
        }

        Term<DeBruijn> shortPkTerm = BuildVerifyTerm(new byte[31], [], validSig);
        Assert.Equal(PublicKeyLengthError, Assert.Throws<EvaluationException>(() => Evaluator.EvaluateTerm(shortPkTerm)).Message);
        Assert.Equal(PublicKeyLengthError, Assert.Throws<EvaluationException>(() => Evaluator.EvaluateScript(
            EncodeBranchScript(shortPkTerm, expectTrue: true),
            Array.Empty<PlutusData>(),
            3,
            11)).Message);

        Term<DeBruijn> shortSigTerm = BuildVerifyTerm(validPk, [], new byte[63]);
        Assert.Equal(SignatureLengthError, Assert.Throws<EvaluationException>(() => Evaluator.EvaluateTerm(shortSigTerm)).Message);
        Assert.Equal(SignatureLengthError, Assert.Throws<EvaluationException>(() => Evaluator.EvaluateScript(
            EncodeBranchScript(shortSigTerm, expectTrue: true),
            Array.Empty<PlutusData>(),
            3,
            11)).Message);
    }

    [Fact]
    public void VerifyEd25519_Wycheproof_MatchesCryptoEd25519_All210Cases()
    {
        (int declaredCount, WycheproofCase[] cases) = LoadWycheproof();
        Assert.Equal(210, declaredCount);
        Assert.Equal(210, cases.Length);
        Assert.Equal(210, cases.Select(c => c.Id).Distinct().Count());

        int non64SigCount = 0;
        int validTrue = 0;
        int mixedTrue = 0;
        int invalidFalse = 0;
        int smallFalse = 0;
        List<int> mismatches = [];

        foreach (WycheproofCase c in cases)
        {
            if (c.Signature.Length != 64)
            {
                non64SigCount++;
                EvaluationException ex = Assert.Throws<EvaluationException>(() => VerifyBuiltin(c.PublicKey, c.Message, c.Signature));
                Assert.Equal(SignatureLengthError, ex.Message);
                continue;
            }

            bool expected = Ed25519.Verify(c.Signature, c.Message, c.PublicKey);
            bool actual = VerifyBuiltin(c.PublicKey, c.Message, c.Signature);
            if (actual != expected)
            {
                mismatches.Add(c.Id);
            }

            if (c.Result == "valid" && expected)
            {
                validTrue++;
            }
            else if (c.Result == "acceptable" && c.Flags.Contains("MixedOrderPublicKey") && expected)
            {
                mixedTrue++;
            }
            else if (c.Result == "invalid" && !expected)
            {
                invalidFalse++;
            }
            else if (c.Result == "acceptable" && c.Flags.Contains("SmallOrderPublicKey") && !expected)
            {
                smallFalse++;
            }
            else
            {
                Assert.Fail($"Unexpected Wycheproof classification for tcId={c.Id} result={c.Result} expected={expected}");
            }
        }

        Assert.True(mismatches.Count == 0, $"Mismatched Wycheproof tcIds: {string.Join(", ", mismatches)}");
        Assert.Equal(12, non64SigCount);
        Assert.Equal(117, validTrue);
        Assert.Equal(21, mixedTrue);
        Assert.Equal(52, invalidFalse);
        Assert.Equal(8, smallFalse);
    }

    [Fact]
    public void VerifyEd25519_ValidRfc8032AndArgumentOrder_BehavesCorrectly()
    {
        Assert.True(VerifyBuiltin(
            Convert.FromHexString(Rfc8032Test1PublicKeyHex),
            [],
            Convert.FromHexString(Rfc8032Test1SignatureHex)));

        Assert.True(VerifyBuiltin(
            Convert.FromHexString(Rfc8032ShaAbcPublicKeyHex),
            Convert.FromHexString(Rfc8032ShaAbcMessageHex),
            Convert.FromHexString(Rfc8032ShaAbcSignatureHex)));

        byte[] seed = Convert.FromHexString(Rfc8032Test1SeedHex);
        byte[] pk = Ed25519.PublicKeyFromSeed(seed);
        byte[] seedAndPk = Ed25519.SeedAndPublicKeyFromSeed(seed);
        byte[] msg32 = CreateBoundaryMessage(32);
        byte[] sig = Ed25519.SignWithSeedAndPublicKey(msg32, seedAndPk);

        Assert.True(VerifyBuiltin(pk, msg32, sig));

        byte[] flippedPk = (byte[])pk.Clone();
        flippedPk[0] ^= 0x01;
        Assert.False(VerifyBuiltin(flippedPk, msg32, sig));

        byte[] flippedMsg = (byte[])msg32.Clone();
        flippedMsg[0] ^= 0x01;
        Assert.False(VerifyBuiltin(pk, flippedMsg, sig));

        byte[] flippedSig = (byte[])sig.Clone();
        flippedSig[0] ^= 0x01;
        Assert.False(VerifyBuiltin(pk, msg32, flippedSig));

        Assert.False(VerifyBuiltin(msg32, pk, sig));

        EvaluationException swappedPkSigEx = Assert.Throws<EvaluationException>(() => VerifyBuiltin(sig, msg32, pk));
        Assert.Equal(PublicKeyLengthError, swappedPkSigEx.Message);
    }

    [Theory]
    [InlineData(0, 64, PublicKeyLengthError)]
    [InlineData(31, 64, PublicKeyLengthError)]
    [InlineData(33, 64, PublicKeyLengthError)]
    [InlineData(64, 64, PublicKeyLengthError)]
    [InlineData(32, 0, SignatureLengthError)]
    [InlineData(32, 32, SignatureLengthError)]
    [InlineData(32, 63, SignatureLengthError)]
    [InlineData(32, 65, SignatureLengthError)]
    [InlineData(31, 63, PublicKeyLengthError)]
    public void VerifyEd25519_InvalidLengths_ThrowsExactEvaluationException(int pkLen, int sigLen, string expectedMessage)
    {
        EvaluationException ex = Assert.Throws<EvaluationException>(
            () => VerifyBuiltin(new byte[pkLen], SampleLengthCheckMessage, new byte[sigLen]));
        Assert.Equal(expectedMessage, ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(960)]
    [InlineData(961)]
    [InlineData(4096)]
    public void VerifyEd25519_MessageLengthBoundaries_MatchCryptoEd25519(int msgLength)
    {
        byte[] seed = Convert.FromHexString(Rfc8032Test1SeedHex);
        byte[] pk = Ed25519.PublicKeyFromSeed(seed);
        byte[] seedAndPk = Ed25519.SeedAndPublicKeyFromSeed(seed);
        byte[] msg = CreateBoundaryMessage(msgLength);
        byte[] sig = Ed25519.SignWithSeedAndPublicKey(msg, seedAndPk);

        Assert.True(Ed25519.Verify(sig, msg, pk));
        Assert.True(VerifyBuiltin(pk, msg, sig));

        byte[] mutatedTarget = msgLength > 0 ? msg : sig;
        mutatedTarget[^1] ^= 0x80;

        Assert.False(Ed25519.Verify(sig, msg, pk));
        Assert.False(VerifyBuiltin(pk, msg, sig));
    }

    private static bool VerifyBuiltin(byte[] pk, byte[] msg, byte[] sig)
    {
        CekValue result = CryptoBuiltins.VerifyEd25519Signature(
        [
            new VConstant(new ByteStringConstant(pk)),
            new VConstant(new ByteStringConstant(msg)),
            new VConstant(new ByteStringConstant(sig)),
        ]);
        return Assert.IsType<BoolConstant>(Assert.IsType<VConstant>(result).Value).Value;
    }

    private static Term<DeBruijn> BuildVerifyTerm(byte[] pk, byte[] msg, byte[] sig) =>
        new ApplyTerm<DeBruijn>(
            new ApplyTerm<DeBruijn>(
                new ApplyTerm<DeBruijn>(
                    new BuiltinTerm<DeBruijn>(DefaultFunction.VerifyEd25519Signature),
                    new ConstTerm<DeBruijn>(new ByteStringConstant(pk))),
                new ConstTerm<DeBruijn>(new ByteStringConstant(msg))),
            new ConstTerm<DeBruijn>(new ByteStringConstant(sig)));

    private static byte[] EncodeBranchScript(Term<DeBruijn> verifyTerm, bool expectTrue)
    {
        Term<DeBruijn> unit = new ConstTerm<DeBruijn>(UnitConstant.Instance);
        Term<DeBruijn> err = new ErrorTerm<DeBruijn>();
        Term<DeBruijn> thenTerm = expectTrue ? unit : err;
        Term<DeBruijn> elseTerm = expectTrue ? err : unit;
        Term<DeBruijn> ite = new ForceTerm<DeBruijn>(new BuiltinTerm<DeBruijn>(DefaultFunction.IfThenElse));
        Term<DeBruijn> branch = new ApplyTerm<DeBruijn>(
            new ApplyTerm<DeBruijn>(
                new ApplyTerm<DeBruijn>(ite, verifyTerm),
                new DelayTerm<DeBruijn>(thenTerm)),
            new DelayTerm<DeBruijn>(elseTerm));
        return FlatEncoder.EncodeProgram(new(new(1, 1, 0), new ForceTerm<DeBruijn>(branch)));
    }

    private static byte[] CreateSmallOrderRSig(string rHex, byte[] pk, byte[] seed)
    {
        byte[] r = Convert.FromHexString(rHex);
        BigInteger k = new(SHA512.HashData([.. r, .. pk]), isUnsigned: true);
        BigInteger s = k * ClampedScalar(seed) % SubgroupOrderL;

        byte[] sig = new byte[64];
        r.CopyTo(sig, 0);
        Assert.True(s.TryWriteBytes(sig.AsSpan(32), out _, isUnsigned: true));
        return sig;
    }

    private static (byte[] PublicKeyMixed, byte[] Message, byte[] Signature) CreateMixedOrderForgery()
    {
        byte[] seed = Convert.FromHexString(Rfc8032Test1SeedHex);
        byte[] pkPrime = Convert.FromHexString(Rfc8032Test1PublicKeyHex);
        byte[] pkMixed = Ed25519.PointAdd(pkPrime, Convert.FromHexString(Order2Hex));
        byte[] r = Convert.FromHexString(BasePointHex);
        BigInteger a = ClampedScalar(seed);

        for (int counter = 0; counter <= byte.MaxValue; counter++)
        {
            byte[] msg = [(byte)counter];
            byte[] challenge = SHA512.HashData([.. r, .. pkMixed, .. msg]);
            BigInteger k = new(challenge, isUnsigned: true);
            BigInteger kModL = k % SubgroupOrderL;
            if (!kModL.IsEven)
            {
                BigInteger s = (BigInteger.One + (kModL * a)) % SubgroupOrderL;
                byte[] sig = new byte[64];
                r.CopyTo(sig, 0);
                Assert.True(s.TryWriteBytes(sig.AsSpan(32), out _, isUnsigned: true));
                return (pkMixed, msg, sig);
            }
        }

        throw new InvalidOperationException("Failed to construct cofactored-only mixed-order forgery.");
    }

    private static BigInteger ClampedScalar(byte[] seed)
    {
        byte[] hash = SHA512.HashData(seed);
        hash[0] &= 248;
        hash[31] &= 127;
        hash[31] |= 64;
        return new(hash.AsSpan(0, 32), isUnsigned: true);
    }

    private static byte[] CreateBoundaryMessage(int length)
    {
        byte[] msg = new byte[length];
        for (int i = 0; i < msg.Length; i++)
        {
            msg[i] = (byte)((i * 31) | 1);
        }

        return msg;
    }

    private static (int DeclaredCount, WycheproofCase[] Cases) LoadWycheproof()
    {
        string path = Path.Combine(
            RepositoryRoot(),
            "src",
            "Chrysalis.Crypto.Test",
            "TestData",
            "Wycheproof",
            "ed25519_test.json");
        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement root = document.RootElement;
        List<WycheproofCase> cases = [];
        foreach (JsonElement group in root.GetProperty("testGroups").EnumerateArray())
        {
            Assert.Equal("EddsaVerify", group.GetProperty("type").GetString());
            byte[] publicKey = Convert.FromHexString(RequiredString(group.GetProperty("publicKey"), "pk"));
            Assert.Equal(32, publicKey.Length);
            foreach (JsonElement entry in group.GetProperty("tests").EnumerateArray())
            {
                cases.Add(new(
                    entry.GetProperty("tcId").GetInt32(),
                    RequiredString(entry, "result"),
                    [.. entry.GetProperty("flags").EnumerateArray().Select(flag => flag.GetString() ?? throw new JsonException("Null flag"))],
                    Convert.FromHexString(RequiredString(entry, "sig")),
                    Convert.FromHexString(RequiredString(entry, "msg")),
                    publicKey));
            }
        }

        return (root.GetProperty("numberOfTests").GetInt32(), [.. cases]);
    }

    private static string RequiredString(JsonElement element, string property) =>
        element.GetProperty(property).GetString() ?? throw new JsonException($"Null {property}");

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
}
