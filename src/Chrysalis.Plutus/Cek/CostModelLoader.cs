using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Cek;

/// <summary>
/// Builds a structured <see cref="BuiltinCostModel"/>[] from a chain "flat" cost-model array
/// (the integer list carried in protocol params / <c>cost_models_raw</c>).
///
/// The flat array is alphabetical-by-parameter-name with the CEK machine costs (<c>cek_*</c>)
/// interleaved; it does NOT line up with the Flat-tag-ordered <see cref="DefaultFunction"/> enum.
/// So we map strictly BY NAME: <see cref="BuildV3CostMap"/> reproduces the canonical position→name
/// table (ported verbatim from aiken's <c>initialize_cost_model</c>, Language::PlutusV3 block), and
/// the builders below read each builtin's parameters by name. A typo therefore fails loud at
/// runtime (missing key) rather than silently reading a wrong index.
///
/// The cost-function SHAPES mirror <see cref="DefaultCosts"/> exactly — only the literals are
/// replaced by chain values. Machine-step costs are NOT handled here (they live in
/// <see cref="MachineCosts"/> and already match the chain).
/// </summary>
internal static class CostModelLoader
{
    /// <summary>
    /// Builds the structured builtin cost model for a script of the given Plutus version.
    /// PlutusV3 uses <paramref name="costs"/> (e.g. the provider's protocol-param model) or, when
    /// null, the baked-in <see cref="DefaultCosts.LatestV3"/>. PlutusV1/V2 don't have a position→name
    /// table ported yet and fall back to the reference <see cref="DefaultCosts.Create"/> model
    /// (matching prior behavior — V1/V2 cost params would otherwise be mis-mapped by the V3 table).
    /// </summary>
    internal static BuiltinCostModel[] FromFlat(IReadOnlyList<long>? costs, int plutusVersion) =>
        plutusVersion == 3
            ? FromFlatV3(costs ?? DefaultCosts.LatestV3)
            : DefaultCosts.Create();

    internal static BuiltinCostModel[] FromFlatV3(IReadOnlyList<long> costs)
    {
        ArgumentNullException.ThrowIfNull(costs);
        if (costs.Count < 297)
        {
            throw new ArgumentException(
                $"PlutusV3 cost model must have at least 297 parameters; got {costs.Count}.",
                nameof(costs));
        }

        Dictionary<string, long> m = BuildV3CostMap(costs);
        long G(string key) => m.TryGetValue(key, out long v)
            ? v
            : throw new KeyNotFoundException($"PlutusV3 cost model is missing parameter '{key}'.");

        // Start from the structured default so builtins absent from the flat V3 array — expMod,
        // dropList, array ops, multiScalarMul, Value ops (enum 87..100, priced only in the pv11
        // tail / future arrays) — keep sane costs. We then overwrite every builtin present below.
        BuiltinCostModel[] c = DefaultCosts.Create();

        // ===== Integer operations =====
        c[(int)DefaultFunction.AddInteger] = new(
            new MaxSizeCost(G("add_integer-cpu-arguments-intercept"), G("add_integer-cpu-arguments-slope")),
            new MaxSizeCost(G("add_integer-mem-arguments-intercept"), G("add_integer-mem-arguments-slope")));

        c[(int)DefaultFunction.SubtractInteger] = new(
            new MaxSizeCost(G("subtract_integer-cpu-arguments-intercept"), G("subtract_integer-cpu-arguments-slope")),
            new MaxSizeCost(G("subtract_integer-mem-arguments-intercept"), G("subtract_integer-mem-arguments-slope")));

        c[(int)DefaultFunction.MultiplyInteger] = new(
            new MultipliedSizesCost(G("multiply_integer-cpu-arguments-intercept"), G("multiply_integer-cpu-arguments-slope")),
            new AddedSizesCost(G("multiply_integer-mem-arguments-intercept"), G("multiply_integer-mem-arguments-slope")));

        // Protocol-11 ("semantics E"): divide/mod/remainder CPU use AboveAndBelowDiagonal (quadratic
        // on the sorted args, both sides of the diagonal — the cpu "constant" param is unused).
        // quotientInteger keeps the constant-below ConstAboveDiagonal shape. mem shapes are unchanged.
        c[(int)DefaultFunction.DivideInteger] = new(
            new AboveAndBelowDiagonalCost(
                G("divide_integer-cpu-arguments-minimum"),
                G("divide_integer-cpu-arguments-c00"), G("divide_integer-cpu-arguments-c10"),
                G("divide_integer-cpu-arguments-c01"), G("divide_integer-cpu-arguments-c20"),
                G("divide_integer-cpu-arguments-c11"), G("divide_integer-cpu-arguments-c02")),
            new SubtractedSizesCost(
                G("divide_integer-mem-arguments-intercept"), G("divide_integer-mem-arguments-slope"),
                G("divide_integer-mem-arguments-minimum")));

        c[(int)DefaultFunction.QuotientInteger] = new(
            new ConstAboveDiagonalCost(
                G("quotient_integer-cpu-arguments-constant"), G("quotient_integer-cpu-arguments-minimum"),
                G("quotient_integer-cpu-arguments-c00"), G("quotient_integer-cpu-arguments-c10"),
                G("quotient_integer-cpu-arguments-c01"), G("quotient_integer-cpu-arguments-c20"),
                G("quotient_integer-cpu-arguments-c11"), G("quotient_integer-cpu-arguments-c02")),
            new SubtractedSizesCost(
                G("quotient_integer-mem-arguments-intercept"), G("quotient_integer-mem-arguments-slope"),
                G("quotient_integer-mem-arguments-minimum")));

        c[(int)DefaultFunction.RemainderInteger] = new(
            new AboveAndBelowDiagonalCost(
                G("remainder_integer-cpu-arguments-minimum"),
                G("remainder_integer-cpu-arguments-c00"), G("remainder_integer-cpu-arguments-c10"),
                G("remainder_integer-cpu-arguments-c01"), G("remainder_integer-cpu-arguments-c20"),
                G("remainder_integer-cpu-arguments-c11"), G("remainder_integer-cpu-arguments-c02")),
            LinearCost.InY(G("remainder_integer-mem-arguments-intercept"), G("remainder_integer-mem-arguments-slope")));

        c[(int)DefaultFunction.ModInteger] = new(
            new AboveAndBelowDiagonalCost(
                G("mod_integer-cpu-arguments-minimum"),
                G("mod_integer-cpu-arguments-c00"), G("mod_integer-cpu-arguments-c10"),
                G("mod_integer-cpu-arguments-c01"), G("mod_integer-cpu-arguments-c20"),
                G("mod_integer-cpu-arguments-c11"), G("mod_integer-cpu-arguments-c02")),
            LinearCost.InY(G("mod_integer-mem-arguments-intercept"), G("mod_integer-mem-arguments-slope")));

        c[(int)DefaultFunction.EqualsInteger] = new(
            new MinSizeCost(G("equals_integer-cpu-arguments-intercept"), G("equals_integer-cpu-arguments-slope")),
            new ConstantCost(G("equals_integer-mem-arguments")));

        c[(int)DefaultFunction.LessThanInteger] = new(
            new MinSizeCost(G("less_than_integer-cpu-arguments-intercept"), G("less_than_integer-cpu-arguments-slope")),
            new ConstantCost(G("less_than_integer-mem-arguments")));

        c[(int)DefaultFunction.LessThanEqualsInteger] = new(
            new MinSizeCost(G("less_than_equals_integer-cpu-arguments-intercept"), G("less_than_equals_integer-cpu-arguments-slope")),
            new ConstantCost(G("less_than_equals_integer-mem-arguments")));

        // ===== ByteString operations =====
        c[(int)DefaultFunction.AppendByteString] = new(
            new AddedSizesCost(G("append_byte_string-cpu-arguments-intercept"), G("append_byte_string-cpu-arguments-slope")),
            new AddedSizesCost(G("append_byte_string-mem-arguments-intercept"), G("append_byte_string-mem-arguments-slope")));

        c[(int)DefaultFunction.ConsByteString] = new(
            LinearCost.InY(G("cons_byte_string-cpu-arguments-intercept"), G("cons_byte_string-cpu-arguments-slope")),
            new AddedSizesCost(G("cons_byte_string-mem-arguments-intercept"), G("cons_byte_string-mem-arguments-slope")));

        c[(int)DefaultFunction.SliceByteString] = new(
            LinearCost.InZ(G("slice_byte_string-cpu-arguments-intercept"), G("slice_byte_string-cpu-arguments-slope")),
            LinearCost.InZ(G("slice_byte_string-mem-arguments-intercept"), G("slice_byte_string-mem-arguments-slope")));

        c[(int)DefaultFunction.LengthOfByteString] = new(
            new ConstantCost(G("length_of_byte_string-cpu-arguments")),
            new ConstantCost(G("length_of_byte_string-mem-arguments")));

        c[(int)DefaultFunction.IndexByteString] = new(
            new ConstantCost(G("index_byte_string-cpu-arguments")),
            new ConstantCost(G("index_byte_string-mem-arguments")));

        c[(int)DefaultFunction.EqualsByteString] = new(
            new LinearOnDiagonalCost(
                G("equals_byte_string-cpu-arguments-intercept"), G("equals_byte_string-cpu-arguments-slope"),
                G("equals_byte_string-cpu-arguments-constant")),
            new ConstantCost(G("equals_byte_string-mem-arguments")));

        c[(int)DefaultFunction.LessThanByteString] = new(
            new MinSizeCost(G("less_than_byte_string-cpu-arguments-intercept"), G("less_than_byte_string-cpu-arguments-slope")),
            new ConstantCost(G("less_than_byte_string-mem-arguments")));

        c[(int)DefaultFunction.LessThanEqualsByteString] = new(
            new MinSizeCost(G("less_than_equals_byte_string-cpu-arguments-intercept"), G("less_than_equals_byte_string-cpu-arguments-slope")),
            new ConstantCost(G("less_than_equals_byte_string-mem-arguments")));

        // ===== Cryptography and hash functions =====
        c[(int)DefaultFunction.Sha2_256] = new(
            LinearCost.InX(G("sha2_256-cpu-arguments-intercept"), G("sha2_256-cpu-arguments-slope")),
            new ConstantCost(G("sha2_256-mem-arguments")));

        c[(int)DefaultFunction.Sha3_256] = new(
            LinearCost.InX(G("sha3_256-cpu-arguments-intercept"), G("sha3_256-cpu-arguments-slope")),
            new ConstantCost(G("sha3_256-mem-arguments")));

        c[(int)DefaultFunction.Blake2b_256] = new(
            LinearCost.InX(G("blake2b_256-cpu-arguments-intercept"), G("blake2b_256-cpu-arguments-slope")),
            new ConstantCost(G("blake2b_256-mem-arguments")));

        c[(int)DefaultFunction.Blake2b_224] = new(
            LinearCost.InX(G("blake2b_224-cpu-arguments-intercept"), G("blake2b_224-cpu-arguments-slope")),
            new ConstantCost(G("blake2b_224-mem-arguments-slope")));

        c[(int)DefaultFunction.Keccak_256] = new(
            LinearCost.InX(G("keccak_256-cpu-arguments-intercept"), G("keccak_256-cpu-arguments-slope")),
            new ConstantCost(G("keccak_256-mem-arguments")));

        c[(int)DefaultFunction.VerifyEd25519Signature] = new(
            LinearCost.InY(G("verify_ed25519_signature-cpu-arguments-intercept"), G("verify_ed25519_signature-cpu-arguments-slope")),
            new ConstantCost(G("verify_ed25519_signature-mem-arguments")));

        c[(int)DefaultFunction.VerifyEcdsaSecp256k1Signature] = new(
            new ConstantCost(G("verify_ecdsa_secp256k1_signature-cpu-arguments")),
            new ConstantCost(G("verify_ecdsa_secp256k1_signature-mem-arguments")));

        c[(int)DefaultFunction.VerifySchnorrSecp256k1Signature] = new(
            LinearCost.InY(G("verify_schnorr_secp256k1_signature-cpu-arguments-intercept"), G("verify_schnorr_secp256k1_signature-cpu-arguments-slope")),
            new ConstantCost(G("verify_schnorr_secp256k1_signature-mem-arguments")));

        c[(int)DefaultFunction.Ripemd_160] = new(
            LinearCost.InX(G("ripemd_160-cpu-arguments-intercept"), G("ripemd_160-cpu-arguments-slope")),
            new ConstantCost(G("ripemd_160-memory-arguments")));

        // ===== String operations =====
        c[(int)DefaultFunction.AppendString] = new(
            new AddedSizesCost(G("append_string-cpu-arguments-intercept"), G("append_string-cpu-arguments-slope")),
            new AddedSizesCost(G("append_string-mem-arguments-intercept"), G("append_string-mem-arguments-slope")));

        c[(int)DefaultFunction.EqualsString] = new(
            new LinearOnDiagonalCost(
                G("equals_string-cpu-arguments-intercept"), G("equals_string-cpu-arguments-slope"),
                G("equals_string-cpu-arguments-constant")),
            new ConstantCost(G("equals_string-mem-arguments")));

        c[(int)DefaultFunction.EncodeUtf8] = new(
            LinearCost.InX(G("encode_utf8-cpu-arguments-intercept"), G("encode_utf8-cpu-arguments-slope")),
            LinearCost.InX(G("encode_utf8-mem-arguments-intercept"), G("encode_utf8-mem-arguments-slope")));

        c[(int)DefaultFunction.DecodeUtf8] = new(
            LinearCost.InX(G("decode_utf8-cpu-arguments-intercept"), G("decode_utf8-cpu-arguments-slope")),
            LinearCost.InX(G("decode_utf8-mem-arguments-intercept"), G("decode_utf8-mem-arguments-slope")));

        // ===== Control flow =====
        c[(int)DefaultFunction.IfThenElse] = new(
            new ConstantCost(G("if_then_else-cpu-arguments")), new ConstantCost(G("if_then_else-mem-arguments")));
        c[(int)DefaultFunction.ChooseUnit] = new(
            new ConstantCost(G("choose_unit-cpu-arguments")), new ConstantCost(G("choose_unit-mem-arguments")));
        c[(int)DefaultFunction.Trace] = new(
            new ConstantCost(G("trace-cpu-arguments")), new ConstantCost(G("trace-mem-arguments")));

        // ===== Pair operations =====
        c[(int)DefaultFunction.FstPair] = new(
            new ConstantCost(G("fst_pair-cpu-arguments")), new ConstantCost(G("fst_pair-mem-arguments")));
        c[(int)DefaultFunction.SndPair] = new(
            new ConstantCost(G("snd_pair-cpu-arguments")), new ConstantCost(G("snd_pair-mem-arguments")));

        // ===== List operations =====
        c[(int)DefaultFunction.ChooseList] = new(
            new ConstantCost(G("choose_list-cpu-arguments")), new ConstantCost(G("choose_list-mem-arguments")));
        c[(int)DefaultFunction.MkCons] = new(
            new ConstantCost(G("mk_cons-cpu-arguments")), new ConstantCost(G("mk_cons-mem-arguments")));
        c[(int)DefaultFunction.HeadList] = new(
            new ConstantCost(G("head_list-cpu-arguments")), new ConstantCost(G("head_list-mem-arguments")));
        c[(int)DefaultFunction.TailList] = new(
            new ConstantCost(G("tail_list-cpu-arguments")), new ConstantCost(G("tail_list-mem-arguments")));
        c[(int)DefaultFunction.NullList] = new(
            new ConstantCost(G("null_list-cpu-arguments")), new ConstantCost(G("null_list-mem-arguments")));

        // ===== Data operations =====
        c[(int)DefaultFunction.ChooseData] = new(
            new ConstantCost(G("choose_data-cpu-arguments")), new ConstantCost(G("choose_data-mem-arguments")));
        c[(int)DefaultFunction.ConstrData] = new(
            new ConstantCost(G("constr_data-cpu-arguments")), new ConstantCost(G("constr_data-mem-arguments")));
        c[(int)DefaultFunction.MapData] = new(
            new ConstantCost(G("map_data-cpu-arguments")), new ConstantCost(G("map_data-mem-arguments")));
        c[(int)DefaultFunction.ListData] = new(
            new ConstantCost(G("list_data-cpu-arguments")), new ConstantCost(G("list_data-mem-arguments")));
        c[(int)DefaultFunction.IData] = new(
            new ConstantCost(G("i_data-cpu-arguments")), new ConstantCost(G("i_data-mem-arguments")));
        c[(int)DefaultFunction.BData] = new(
            new ConstantCost(G("b_data-cpu-arguments")), new ConstantCost(G("b_data-mem-arguments")));
        c[(int)DefaultFunction.UnConstrData] = new(
            new ConstantCost(G("un_constr_data-cpu-arguments")), new ConstantCost(G("un_constr_data-mem-arguments")));
        c[(int)DefaultFunction.UnMapData] = new(
            new ConstantCost(G("un_map_data-cpu-arguments")), new ConstantCost(G("un_map_data-mem-arguments")));
        c[(int)DefaultFunction.UnListData] = new(
            new ConstantCost(G("un_list_data-cpu-arguments")), new ConstantCost(G("un_list_data-mem-arguments")));
        c[(int)DefaultFunction.UnIData] = new(
            new ConstantCost(G("un_i_data-cpu-arguments")), new ConstantCost(G("un_i_data-mem-arguments")));
        c[(int)DefaultFunction.UnBData] = new(
            new ConstantCost(G("un_b_data-cpu-arguments")), new ConstantCost(G("un_b_data-mem-arguments")));

        c[(int)DefaultFunction.EqualsData] = new(
            new MinSizeCost(G("equals_data-cpu-arguments-intercept"), G("equals_data-cpu-arguments-slope")),
            new ConstantCost(G("equals_data-mem-arguments")));

        c[(int)DefaultFunction.MkPairData] = new(
            new ConstantCost(G("mk_pair_data-cpu-arguments")), new ConstantCost(G("mk_pair_data-mem-arguments")));
        c[(int)DefaultFunction.MkNilData] = new(
            new ConstantCost(G("mk_nil_data-cpu-arguments")), new ConstantCost(G("mk_nil_data-mem-arguments")));
        c[(int)DefaultFunction.MkNilPairData] = new(
            new ConstantCost(G("mk_nil_pair_data-cpu-arguments")), new ConstantCost(G("mk_nil_pair_data-mem-arguments")));

        c[(int)DefaultFunction.SerialiseData] = new(
            LinearCost.InX(G("serialise_data-cpu-arguments-intercept"), G("serialise_data-cpu-arguments-slope")),
            LinearCost.InX(G("serialise_data-mem-arguments-intercept"), G("serialise_data-mem-arguments-slope")));

        // ===== BLS12-381 G1 =====
        c[(int)DefaultFunction.Bls12_381_G1_Add] = new(
            new ConstantCost(G("bls12_381_G1_add-cpu-arguments")), new ConstantCost(G("bls12_381_G1_add-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_G1_Neg] = new(
            new ConstantCost(G("bls12_381_G1_neg-cpu-arguments")), new ConstantCost(G("bls12_381_G1_neg-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_G1_ScalarMul] = new(
            LinearCost.InX(G("bls12_381_G1_scalarMul-cpu-arguments-intercept"), G("bls12_381_G1_scalarMul-cpu-arguments-slope")),
            new ConstantCost(G("bls12_381_G1_scalarMul-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_G1_Equal] = new(
            new ConstantCost(G("bls12_381_G1_equal-cpu-arguments")), new ConstantCost(G("bls12_381_G1_equal-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_G1_Compress] = new(
            new ConstantCost(G("bls12_381_G1_compress-cpu-arguments")), new ConstantCost(G("bls12_381_G1_compress-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_G1_Uncompress] = new(
            new ConstantCost(G("bls12_381_G1_uncompress-cpu-arguments")), new ConstantCost(G("bls12_381_G1_uncompress-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_G1_HashToGroup] = new(
            LinearCost.InX(G("bls12_381_G1_hashToGroup-cpu-arguments-intercept"), G("bls12_381_G1_hashToGroup-cpu-arguments-slope")),
            new ConstantCost(G("bls12_381_G1_hashToGroup-mem-arguments")));

        // ===== BLS12-381 G2 =====
        c[(int)DefaultFunction.Bls12_381_G2_Add] = new(
            new ConstantCost(G("bls12_381_G2_add-cpu-arguments")), new ConstantCost(G("bls12_381_G2_add-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_G2_Neg] = new(
            new ConstantCost(G("bls12_381_G2_neg-cpu-arguments")), new ConstantCost(G("bls12_381_G2_neg-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_G2_ScalarMul] = new(
            LinearCost.InX(G("bls12_381_G2_scalarMul-cpu-arguments-intercept"), G("bls12_381_G2_scalarMul-cpu-arguments-slope")),
            new ConstantCost(G("bls12_381_G2_scalarMul-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_G2_Equal] = new(
            new ConstantCost(G("bls12_381_G2_equal-cpu-arguments")), new ConstantCost(G("bls12_381_G2_equal-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_G2_Compress] = new(
            new ConstantCost(G("bls12_381_G2_compress-cpu-arguments")), new ConstantCost(G("bls12_381_G2_compress-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_G2_Uncompress] = new(
            new ConstantCost(G("bls12_381_G2_uncompress-cpu-arguments")), new ConstantCost(G("bls12_381_G2_uncompress-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_G2_HashToGroup] = new(
            LinearCost.InX(G("bls12_381_G2_hashToGroup-cpu-arguments-intercept"), G("bls12_381_G2_hashToGroup-cpu-arguments-slope")),
            new ConstantCost(G("bls12_381_G2_hashToGroup-mem-arguments")));

        // ===== BLS12-381 pairing =====
        c[(int)DefaultFunction.Bls12_381_MillerLoop] = new(
            new ConstantCost(G("bls12_381_millerLoop-cpu-arguments")), new ConstantCost(G("bls12_381_millerLoop-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_MulMlResult] = new(
            new ConstantCost(G("bls12_381_mulMlResult-cpu-arguments")), new ConstantCost(G("bls12_381_mulMlResult-mem-arguments")));
        c[(int)DefaultFunction.Bls12_381_FinalVerify] = new(
            new ConstantCost(G("bls12_381_finalVerify-cpu-arguments")), new ConstantCost(G("bls12_381_finalVerify-mem-arguments")));

        // ===== Byte/Integer conversion =====
        c[(int)DefaultFunction.IntegerToByteString] = new(
            QuadraticCost.InZ(
                G("integerToByteString-cpu-arguments-c0"), G("integerToByteString-cpu-arguments-c1"),
                G("integerToByteString-cpu-arguments-c2")),
            new LiteralInYOrLinearInZCost(
                G("integerToByteString-mem-arguments-intercept"), G("integerToByteString-mem-arguments-slope")));

        c[(int)DefaultFunction.ByteStringToInteger] = new(
            QuadraticCost.InY(
                G("byteStringToInteger-cpu-arguments-c0"), G("byteStringToInteger-cpu-arguments-c1"),
                G("byteStringToInteger-cpu-arguments-c2")),
            LinearCost.InY(
                G("byteStringToInteger-mem-arguments-intercept"), G("byteStringToInteger-mem-arguments-slope")));

        // ===== Bitwise operations (present when Count >= 297, guaranteed above) =====
        c[(int)DefaultFunction.AndByteString] = new(
            new LinearInYAndZCost(G("andByteString-cpu-arguments-intercept"), G("andByteString-cpu-arguments-slope1"), G("andByteString-cpu-arguments-slope2")),
            new LinearInMaxYZCost(G("andByteString-memory-arguments-intercept"), G("andByteString-memory-arguments-slope")));
        c[(int)DefaultFunction.OrByteString] = new(
            new LinearInYAndZCost(G("orByteString-cpu-arguments-intercept"), G("orByteString-cpu-arguments-slope1"), G("orByteString-cpu-arguments-slope2")),
            new LinearInMaxYZCost(G("orByteString-memory-arguments-intercept"), G("orByteString-memory-arguments-slope")));
        c[(int)DefaultFunction.XorByteString] = new(
            new LinearInYAndZCost(G("xorByteString-cpu-arguments-intercept"), G("xorByteString-cpu-arguments-slope1"), G("xorByteString-cpu-arguments-slope2")),
            new LinearInMaxYZCost(G("xorByteString-memory-arguments-intercept"), G("xorByteString-memory-arguments-slope")));
        c[(int)DefaultFunction.ComplementByteString] = new(
            LinearCost.InX(G("complementByteString-cpu-arguments-intercept"), G("complementByteString-cpu-arguments-slope")),
            LinearCost.InX(G("complementByteString-memory-arguments-intercept"), G("complementByteString-memory-arguments-slope")));
        c[(int)DefaultFunction.ReadBit] = new(
            new ConstantCost(G("readBit-cpu-arguments")), new ConstantCost(G("readBit-memory-arguments")));
        c[(int)DefaultFunction.WriteBits] = new(
            LinearCost.InY(G("writeBits-cpu-arguments-intercept"), G("writeBits-cpu-arguments-slope")),
            LinearCost.InX(G("writeBits-memory-arguments-intercept"), G("writeBits-memory-arguments-slope")));
        c[(int)DefaultFunction.ReplicateByte] = new(
            LinearCost.InX(G("replicateByte-cpu-arguments-intercept"), G("replicateByte-cpu-arguments-slope")),
            LinearCost.InX(G("replicateByte-memory-arguments-intercept"), G("replicateByte-memory-arguments-slope")));
        c[(int)DefaultFunction.ShiftByteString] = new(
            LinearCost.InX(G("shiftByteString-cpu-arguments-intercept"), G("shiftByteString-cpu-arguments-slope")),
            LinearCost.InX(G("shiftByteString-memory-arguments-intercept"), G("shiftByteString-memory-arguments-slope")));
        c[(int)DefaultFunction.RotateByteString] = new(
            LinearCost.InX(G("rotateByteString-cpu-arguments-intercept"), G("rotateByteString-cpu-arguments-slope")),
            LinearCost.InX(G("rotateByteString-memory-arguments-intercept"), G("rotateByteString-memory-arguments-slope")));
        c[(int)DefaultFunction.CountSetBits] = new(
            LinearCost.InX(G("countSetBits-cpu-arguments-intercept"), G("countSetBits-cpu-arguments-slope")),
            new ConstantCost(G("countSetBits-memory-arguments")));
        c[(int)DefaultFunction.FindFirstSetBit] = new(
            LinearCost.InX(G("findFirstSetBit-cpu-arguments-intercept"), G("findFirstSetBit-cpu-arguments-slope")),
            new ConstantCost(G("findFirstSetBit-memory-arguments")));

        return c;
    }

    /// <summary>
    /// Position→name table for the PlutusV3 flat cost array. Ported verbatim from aiken's
    /// <c>initialize_cost_model</c> (<c>Language::PlutusV3</c>). Base entries cover indices 0..250;
    /// the bitwise/RIPEMD block (251..296) is appended (aiken gates it on <c>len >= 297</c>, which
    /// the caller has already enforced). Indices 297+ are the pv11 tail (builtins Chrysalis does not
    /// yet model) and are intentionally not mapped.
    /// </summary>
    private static Dictionary<string, long> BuildV3CostMap(IReadOnlyList<long> c)
    {
        Dictionary<string, long> m = new(350)
        {
            ["add_integer-cpu-arguments-intercept"] = c[0],
            ["add_integer-cpu-arguments-slope"] = c[1],
            ["add_integer-mem-arguments-intercept"] = c[2],
            ["add_integer-mem-arguments-slope"] = c[3],
            ["append_byte_string-cpu-arguments-intercept"] = c[4],
            ["append_byte_string-cpu-arguments-slope"] = c[5],
            ["append_byte_string-mem-arguments-intercept"] = c[6],
            ["append_byte_string-mem-arguments-slope"] = c[7],
            ["append_string-cpu-arguments-intercept"] = c[8],
            ["append_string-cpu-arguments-slope"] = c[9],
            ["append_string-mem-arguments-intercept"] = c[10],
            ["append_string-mem-arguments-slope"] = c[11],
            ["b_data-cpu-arguments"] = c[12],
            ["b_data-mem-arguments"] = c[13],
            ["blake2b_256-cpu-arguments-intercept"] = c[14],
            ["blake2b_256-cpu-arguments-slope"] = c[15],
            ["blake2b_256-mem-arguments"] = c[16],
            ["cek_apply_cost-exBudgetCPU"] = c[17],
            ["cek_apply_cost-exBudgetmem"] = c[18],
            ["cek_builtin_cost-exBudgetCPU"] = c[19],
            ["cek_builtin_cost-exBudgetmem"] = c[20],
            ["cek_const_cost-exBudgetCPU"] = c[21],
            ["cek_const_cost-exBudgetmem"] = c[22],
            ["cek_delay_cost-exBudgetCPU"] = c[23],
            ["cek_delay_cost-exBudgetmem"] = c[24],
            ["cek_force_cost-exBudgetCPU"] = c[25],
            ["cek_force_cost-exBudgetmem"] = c[26],
            ["cek_lam_cost-exBudgetCPU"] = c[27],
            ["cek_lam_cost-exBudgetmem"] = c[28],
            ["cek_startup_cost-exBudgetCPU"] = c[29],
            ["cek_startup_cost-exBudgetmem"] = c[30],
            ["cek_var_cost-exBudgetCPU"] = c[31],
            ["cek_var_cost-exBudgetmem"] = c[32],
            ["choose_data-cpu-arguments"] = c[33],
            ["choose_data-mem-arguments"] = c[34],
            ["choose_list-cpu-arguments"] = c[35],
            ["choose_list-mem-arguments"] = c[36],
            ["choose_unit-cpu-arguments"] = c[37],
            ["choose_unit-mem-arguments"] = c[38],
            ["cons_byte_string-cpu-arguments-intercept"] = c[39],
            ["cons_byte_string-cpu-arguments-slope"] = c[40],
            ["cons_byte_string-mem-arguments-intercept"] = c[41],
            ["cons_byte_string-mem-arguments-slope"] = c[42],
            ["constr_data-cpu-arguments"] = c[43],
            ["constr_data-mem-arguments"] = c[44],
            ["decode_utf8-cpu-arguments-intercept"] = c[45],
            ["decode_utf8-cpu-arguments-slope"] = c[46],
            ["decode_utf8-mem-arguments-intercept"] = c[47],
            ["decode_utf8-mem-arguments-slope"] = c[48],
            ["divide_integer-cpu-arguments-constant"] = c[49],
            ["divide_integer-cpu-arguments-c00"] = c[50],
            ["divide_integer-cpu-arguments-c01"] = c[51],
            ["divide_integer-cpu-arguments-c02"] = c[52],
            ["divide_integer-cpu-arguments-c10"] = c[53],
            ["divide_integer-cpu-arguments-c11"] = c[54],
            ["divide_integer-cpu-arguments-c20"] = c[55],
            ["divide_integer-cpu-arguments-minimum"] = c[56],
            ["divide_integer-mem-arguments-intercept"] = c[57],
            ["divide_integer-mem-arguments-minimum"] = c[58],
            ["divide_integer-mem-arguments-slope"] = c[59],
            ["encode_utf8-cpu-arguments-intercept"] = c[60],
            ["encode_utf8-cpu-arguments-slope"] = c[61],
            ["encode_utf8-mem-arguments-intercept"] = c[62],
            ["encode_utf8-mem-arguments-slope"] = c[63],
            ["equals_byte_string-cpu-arguments-constant"] = c[64],
            ["equals_byte_string-cpu-arguments-intercept"] = c[65],
            ["equals_byte_string-cpu-arguments-slope"] = c[66],
            ["equals_byte_string-mem-arguments"] = c[67],
            ["equals_data-cpu-arguments-intercept"] = c[68],
            ["equals_data-cpu-arguments-slope"] = c[69],
            ["equals_data-mem-arguments"] = c[70],
            ["equals_integer-cpu-arguments-intercept"] = c[71],
            ["equals_integer-cpu-arguments-slope"] = c[72],
            ["equals_integer-mem-arguments"] = c[73],
            ["equals_string-cpu-arguments-constant"] = c[74],
            ["equals_string-cpu-arguments-intercept"] = c[75],
            ["equals_string-cpu-arguments-slope"] = c[76],
            ["equals_string-mem-arguments"] = c[77],
            ["fst_pair-cpu-arguments"] = c[78],
            ["fst_pair-mem-arguments"] = c[79],
            ["head_list-cpu-arguments"] = c[80],
            ["head_list-mem-arguments"] = c[81],
            ["i_data-cpu-arguments"] = c[82],
            ["i_data-mem-arguments"] = c[83],
            ["if_then_else-cpu-arguments"] = c[84],
            ["if_then_else-mem-arguments"] = c[85],
            ["index_byte_string-cpu-arguments"] = c[86],
            ["index_byte_string-mem-arguments"] = c[87],
            ["length_of_byte_string-cpu-arguments"] = c[88],
            ["length_of_byte_string-mem-arguments"] = c[89],
            ["less_than_byte_string-cpu-arguments-intercept"] = c[90],
            ["less_than_byte_string-cpu-arguments-slope"] = c[91],
            ["less_than_byte_string-mem-arguments"] = c[92],
            ["less_than_equals_byte_string-cpu-arguments-intercept"] = c[93],
            ["less_than_equals_byte_string-cpu-arguments-slope"] = c[94],
            ["less_than_equals_byte_string-mem-arguments"] = c[95],
            ["less_than_equals_integer-cpu-arguments-intercept"] = c[96],
            ["less_than_equals_integer-cpu-arguments-slope"] = c[97],
            ["less_than_equals_integer-mem-arguments"] = c[98],
            ["less_than_integer-cpu-arguments-intercept"] = c[99],
            ["less_than_integer-cpu-arguments-slope"] = c[100],
            ["less_than_integer-mem-arguments"] = c[101],
            ["list_data-cpu-arguments"] = c[102],
            ["list_data-mem-arguments"] = c[103],
            ["map_data-cpu-arguments"] = c[104],
            ["map_data-mem-arguments"] = c[105],
            ["mk_cons-cpu-arguments"] = c[106],
            ["mk_cons-mem-arguments"] = c[107],
            ["mk_nil_data-cpu-arguments"] = c[108],
            ["mk_nil_data-mem-arguments"] = c[109],
            ["mk_nil_pair_data-cpu-arguments"] = c[110],
            ["mk_nil_pair_data-mem-arguments"] = c[111],
            ["mk_pair_data-cpu-arguments"] = c[112],
            ["mk_pair_data-mem-arguments"] = c[113],
            ["mod_integer-cpu-arguments-constant"] = c[114],
            ["mod_integer-cpu-arguments-c00"] = c[115],
            ["mod_integer-cpu-arguments-c01"] = c[116],
            ["mod_integer-cpu-arguments-c02"] = c[117],
            ["mod_integer-cpu-arguments-c10"] = c[118],
            ["mod_integer-cpu-arguments-c11"] = c[119],
            ["mod_integer-cpu-arguments-c20"] = c[120],
            ["mod_integer-cpu-arguments-minimum"] = c[121],
            ["mod_integer-mem-arguments-intercept"] = c[122],
            ["mod_integer-mem-arguments-slope"] = c[123],
            ["multiply_integer-cpu-arguments-intercept"] = c[124],
            ["multiply_integer-cpu-arguments-slope"] = c[125],
            ["multiply_integer-mem-arguments-intercept"] = c[126],
            ["multiply_integer-mem-arguments-slope"] = c[127],
            ["null_list-cpu-arguments"] = c[128],
            ["null_list-mem-arguments"] = c[129],
            ["quotient_integer-cpu-arguments-constant"] = c[130],
            ["quotient_integer-cpu-arguments-c00"] = c[131],
            ["quotient_integer-cpu-arguments-c01"] = c[132],
            ["quotient_integer-cpu-arguments-c02"] = c[133],
            ["quotient_integer-cpu-arguments-c10"] = c[134],
            ["quotient_integer-cpu-arguments-c11"] = c[135],
            ["quotient_integer-cpu-arguments-c20"] = c[136],
            ["quotient_integer-cpu-arguments-minimum"] = c[137],
            ["quotient_integer-mem-arguments-intercept"] = c[138],
            ["quotient_integer-mem-arguments-minimum"] = c[139],
            ["quotient_integer-mem-arguments-slope"] = c[140],
            ["remainder_integer-cpu-arguments-constant"] = c[141],
            ["remainder_integer-cpu-arguments-c00"] = c[142],
            ["remainder_integer-cpu-arguments-c01"] = c[143],
            ["remainder_integer-cpu-arguments-c02"] = c[144],
            ["remainder_integer-cpu-arguments-c10"] = c[145],
            ["remainder_integer-cpu-arguments-c11"] = c[146],
            ["remainder_integer-cpu-arguments-c20"] = c[147],
            ["remainder_integer-cpu-arguments-minimum"] = c[148],
            ["remainder_integer-mem-arguments-intercept"] = c[149],
            ["remainder_integer-mem-arguments-slope"] = c[150],
            ["serialise_data-cpu-arguments-intercept"] = c[151],
            ["serialise_data-cpu-arguments-slope"] = c[152],
            ["serialise_data-mem-arguments-intercept"] = c[153],
            ["serialise_data-mem-arguments-slope"] = c[154],
            ["sha2_256-cpu-arguments-intercept"] = c[155],
            ["sha2_256-cpu-arguments-slope"] = c[156],
            ["sha2_256-mem-arguments"] = c[157],
            ["sha3_256-cpu-arguments-intercept"] = c[158],
            ["sha3_256-cpu-arguments-slope"] = c[159],
            ["sha3_256-mem-arguments"] = c[160],
            ["slice_byte_string-cpu-arguments-intercept"] = c[161],
            ["slice_byte_string-cpu-arguments-slope"] = c[162],
            ["slice_byte_string-mem-arguments-intercept"] = c[163],
            ["slice_byte_string-mem-arguments-slope"] = c[164],
            ["snd_pair-cpu-arguments"] = c[165],
            ["snd_pair-mem-arguments"] = c[166],
            ["subtract_integer-cpu-arguments-intercept"] = c[167],
            ["subtract_integer-cpu-arguments-slope"] = c[168],
            ["subtract_integer-mem-arguments-intercept"] = c[169],
            ["subtract_integer-mem-arguments-slope"] = c[170],
            ["tail_list-cpu-arguments"] = c[171],
            ["tail_list-mem-arguments"] = c[172],
            ["trace-cpu-arguments"] = c[173],
            ["trace-mem-arguments"] = c[174],
            ["un_b_data-cpu-arguments"] = c[175],
            ["un_b_data-mem-arguments"] = c[176],
            ["un_constr_data-cpu-arguments"] = c[177],
            ["un_constr_data-mem-arguments"] = c[178],
            ["un_i_data-cpu-arguments"] = c[179],
            ["un_i_data-mem-arguments"] = c[180],
            ["un_list_data-cpu-arguments"] = c[181],
            ["un_list_data-mem-arguments"] = c[182],
            ["un_map_data-cpu-arguments"] = c[183],
            ["un_map_data-mem-arguments"] = c[184],
            ["verify_ecdsa_secp256k1_signature-cpu-arguments"] = c[185],
            ["verify_ecdsa_secp256k1_signature-mem-arguments"] = c[186],
            ["verify_ed25519_signature-cpu-arguments-intercept"] = c[187],
            ["verify_ed25519_signature-cpu-arguments-slope"] = c[188],
            ["verify_ed25519_signature-mem-arguments"] = c[189],
            ["verify_schnorr_secp256k1_signature-cpu-arguments-intercept"] = c[190],
            ["verify_schnorr_secp256k1_signature-cpu-arguments-slope"] = c[191],
            ["verify_schnorr_secp256k1_signature-mem-arguments"] = c[192],
            ["cek_constr_cost-exBudgetCPU"] = c[193],
            ["cek_constr_cost-exBudgetmem"] = c[194],
            ["cek_case_cost-exBudgetCPU"] = c[195],
            ["cek_case_cost-exBudgetmem"] = c[196],
            ["bls12_381_G1_add-cpu-arguments"] = c[197],
            ["bls12_381_G1_add-mem-arguments"] = c[198],
            ["bls12_381_G1_compress-cpu-arguments"] = c[199],
            ["bls12_381_G1_compress-mem-arguments"] = c[200],
            ["bls12_381_G1_equal-cpu-arguments"] = c[201],
            ["bls12_381_G1_equal-mem-arguments"] = c[202],
            ["bls12_381_G1_hashToGroup-cpu-arguments-intercept"] = c[203],
            ["bls12_381_G1_hashToGroup-cpu-arguments-slope"] = c[204],
            ["bls12_381_G1_hashToGroup-mem-arguments"] = c[205],
            ["bls12_381_G1_neg-cpu-arguments"] = c[206],
            ["bls12_381_G1_neg-mem-arguments"] = c[207],
            ["bls12_381_G1_scalarMul-cpu-arguments-intercept"] = c[208],
            ["bls12_381_G1_scalarMul-cpu-arguments-slope"] = c[209],
            ["bls12_381_G1_scalarMul-mem-arguments"] = c[210],
            ["bls12_381_G1_uncompress-cpu-arguments"] = c[211],
            ["bls12_381_G1_uncompress-mem-arguments"] = c[212],
            ["bls12_381_G2_add-cpu-arguments"] = c[213],
            ["bls12_381_G2_add-mem-arguments"] = c[214],
            ["bls12_381_G2_compress-cpu-arguments"] = c[215],
            ["bls12_381_G2_compress-mem-arguments"] = c[216],
            ["bls12_381_G2_equal-cpu-arguments"] = c[217],
            ["bls12_381_G2_equal-mem-arguments"] = c[218],
            ["bls12_381_G2_hashToGroup-cpu-arguments-intercept"] = c[219],
            ["bls12_381_G2_hashToGroup-cpu-arguments-slope"] = c[220],
            ["bls12_381_G2_hashToGroup-mem-arguments"] = c[221],
            ["bls12_381_G2_neg-cpu-arguments"] = c[222],
            ["bls12_381_G2_neg-mem-arguments"] = c[223],
            ["bls12_381_G2_scalarMul-cpu-arguments-intercept"] = c[224],
            ["bls12_381_G2_scalarMul-cpu-arguments-slope"] = c[225],
            ["bls12_381_G2_scalarMul-mem-arguments"] = c[226],
            ["bls12_381_G2_uncompress-cpu-arguments"] = c[227],
            ["bls12_381_G2_uncompress-mem-arguments"] = c[228],
            ["bls12_381_finalVerify-cpu-arguments"] = c[229],
            ["bls12_381_finalVerify-mem-arguments"] = c[230],
            ["bls12_381_millerLoop-cpu-arguments"] = c[231],
            ["bls12_381_millerLoop-mem-arguments"] = c[232],
            ["bls12_381_mulMlResult-cpu-arguments"] = c[233],
            ["bls12_381_mulMlResult-mem-arguments"] = c[234],
            ["keccak_256-cpu-arguments-intercept"] = c[235],
            ["keccak_256-cpu-arguments-slope"] = c[236],
            ["keccak_256-mem-arguments"] = c[237],
            ["blake2b_224-cpu-arguments-intercept"] = c[238],
            ["blake2b_224-cpu-arguments-slope"] = c[239],
            ["blake2b_224-mem-arguments-slope"] = c[240],
            ["integerToByteString-cpu-arguments-c0"] = c[241],
            ["integerToByteString-cpu-arguments-c1"] = c[242],
            ["integerToByteString-cpu-arguments-c2"] = c[243],
            ["integerToByteString-mem-arguments-intercept"] = c[244],
            ["integerToByteString-mem-arguments-slope"] = c[245],
            ["byteStringToInteger-cpu-arguments-c0"] = c[246],
            ["byteStringToInteger-cpu-arguments-c1"] = c[247],
            ["byteStringToInteger-cpu-arguments-c2"] = c[248],
            ["byteStringToInteger-mem-arguments-intercept"] = c[249],
            ["byteStringToInteger-mem-arguments-slope"] = c[250],
        };

        // Bitwise + RIPEMD-160 (aiken appends these when len >= 297).
        m["andByteString-cpu-arguments-intercept"] = c[251];
        m["andByteString-cpu-arguments-slope1"] = c[252];
        m["andByteString-cpu-arguments-slope2"] = c[253];
        m["andByteString-memory-arguments-intercept"] = c[254];
        m["andByteString-memory-arguments-slope"] = c[255];
        m["orByteString-cpu-arguments-intercept"] = c[256];
        m["orByteString-cpu-arguments-slope1"] = c[257];
        m["orByteString-cpu-arguments-slope2"] = c[258];
        m["orByteString-memory-arguments-intercept"] = c[259];
        m["orByteString-memory-arguments-slope"] = c[260];
        m["xorByteString-cpu-arguments-intercept"] = c[261];
        m["xorByteString-cpu-arguments-slope1"] = c[262];
        m["xorByteString-cpu-arguments-slope2"] = c[263];
        m["xorByteString-memory-arguments-intercept"] = c[264];
        m["xorByteString-memory-arguments-slope"] = c[265];
        m["complementByteString-cpu-arguments-intercept"] = c[266];
        m["complementByteString-cpu-arguments-slope"] = c[267];
        m["complementByteString-memory-arguments-intercept"] = c[268];
        m["complementByteString-memory-arguments-slope"] = c[269];
        m["readBit-cpu-arguments"] = c[270];
        m["readBit-memory-arguments"] = c[271];
        m["writeBits-cpu-arguments-intercept"] = c[272];
        m["writeBits-cpu-arguments-slope"] = c[273];
        m["writeBits-memory-arguments-intercept"] = c[274];
        m["writeBits-memory-arguments-slope"] = c[275];
        m["replicateByte-cpu-arguments-intercept"] = c[276];
        m["replicateByte-cpu-arguments-slope"] = c[277];
        m["replicateByte-memory-arguments-intercept"] = c[278];
        m["replicateByte-memory-arguments-slope"] = c[279];
        m["shiftByteString-cpu-arguments-intercept"] = c[280];
        m["shiftByteString-cpu-arguments-slope"] = c[281];
        m["shiftByteString-memory-arguments-intercept"] = c[282];
        m["shiftByteString-memory-arguments-slope"] = c[283];
        m["rotateByteString-cpu-arguments-intercept"] = c[284];
        m["rotateByteString-cpu-arguments-slope"] = c[285];
        m["rotateByteString-memory-arguments-intercept"] = c[286];
        m["rotateByteString-memory-arguments-slope"] = c[287];
        m["countSetBits-cpu-arguments-intercept"] = c[288];
        m["countSetBits-cpu-arguments-slope"] = c[289];
        m["countSetBits-memory-arguments"] = c[290];
        m["findFirstSetBit-cpu-arguments-intercept"] = c[291];
        m["findFirstSetBit-cpu-arguments-slope"] = c[292];
        m["findFirstSetBit-memory-arguments"] = c[293];
        m["ripemd_160-cpu-arguments-intercept"] = c[294];
        m["ripemd_160-cpu-arguments-slope"] = c[295];
        m["ripemd_160-memory-arguments"] = c[296];

        return m;
    }
}
