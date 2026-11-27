using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using SAIB.Cbor;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Serialization;

/// <summary>Wire-validation and encoding helpers invoked by generated official <c>[Union]</c> struct codecs.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CborUnionWire
{
    /// <summary>Major-type mask value representing CBOR booleans (<c>0xF4</c> / <c>0xF5</c>).</summary>
    public const int BooleanMask = 1 << 8;

    /// <summary>Returns <paramref name="value"/>'s existing wire slice or encodes it fresh once without boxing.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlyMemory<byte> GetOrEncodeRaw<TCase>(TCase value, WriteHandler<TCase> writer, string unionPath)
        where TCase : ICborType
    {
        if (value is null) { throw new ArgumentNullException(nameof(value)); }
        return !value.Raw.IsEmpty ? value.Raw : EncodeFresh(value, writer, unionPath);
    }

    /// <summary>Encodes an empty-wire case once via its generated writer and fails closed on empty or decode failures.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static ReadOnlyMemory<byte> EncodeFresh<TCase>(TCase value, WriteHandler<TCase> writer, string unionPath)
        where TCase : ICborType
    {
        if (value is null) { throw new ArgumentNullException(nameof(value)); }
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(unionPath);

        ArrayBufferWriter<byte> buffer = new();
        try { writer(buffer, value); }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            throw error is CborValidationException validation
                ? validation
                : CborError.Validation(-1, unionPath, "valid union case", error.Message, error);
        }

        return buffer.WrittenCount > 0
            ? buffer.WrittenMemory
            : throw CborError.Validation(-1, unionPath, "non-empty encoded union case", "empty output");
    }

    /// <summary>Writes a non-default union value's wire slice directly to <paramref name="output"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteRaw(IBufferWriter<byte> output, byte kind, ReadOnlyMemory<byte> raw, string unionPath)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(unionPath);
        if (kind == 0 || raw.IsEmpty) { throw ThrowUninitialized(unionPath); }
        output.Write(raw.Span);
    }

    /// <summary>Validates that a non-list union read consumed exactly one complete CBOR item.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlyMemory<byte> ValidateReadSlice(ReadOnlyMemory<byte> data, int bytesConsumed, string unionPath) =>
        ValidateCompleteCore(data, bytesConsumed, unionPath, asValidation: false);

    /// <summary>Validates a <c>[CborList]</c> union read in one pass for structure, optional tag, discriminant, and arity.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlyMemory<byte> ValidateReadListSlice(
        ReadOnlyMemory<byte> data, int bytesConsumed, int? expectedIndex, int minItems, int maxItems, int? expectedTag, string unionPath,
        int secondaryDepth = -1, int secondaryMask = 0, int? outerTag = null) =>
        ValidateArrayCore(data, bytesConsumed, expectedIndex, minItems, maxItems, expectedTag, unionPath, asValidation: false, secondaryDepth, secondaryMask, outerTag);

    /// <summary>Validates a tagged union read slice (exact or dynamic tag, optionally requiring an array payload).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlyMemory<byte> ValidateReadTagSlice(ReadOnlyMemory<byte> data, int bytesConsumed, int expectedTag, bool requireArray, string unionPath, int? outerTag = null) =>
        ValidateTagCore(data, bytesConsumed, expectedTag, requireArray, unionPath, asValidation: false, outerTag);

    /// <summary>Validates a container or map union read slice against its root CBOR major-type mask without skipping tags.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ReadOnlyMemory<byte> ValidateReadMajorTypeSlice(ReadOnlyMemory<byte> data, int bytesConsumed, int expectedMajorMask, string unionPath) =>
        ValidateMajorTypeCore(data, bytesConsumed, expectedMajorMask, unionPath, asValidation: false);

    /// <summary>Bounded dispatch lookahead: returns the exact item count up to the cap (<paramref name="maxNeeded"/>, or zero if negative), cap plus one as soon as the count exceeds it (without scanning further indefinite items), or <c>-1</c> if an indefinite array at or below the cap is unterminated.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ReadArrayLength(ReadOnlySpan<byte> data, int maxNeeded)
    {
        int cap = maxNeeded < 0 ? 0 : maxNeeded;
        CborReader reader = new(data);
        reader.ReadBeginArray();
        int size = reader.ReadSize();
        if (size >= 0) { return size > cap ? cap + 1 : size; }
        int count = 0;
        while (!reader.Buffer.IsEmpty && reader.Buffer[0] != 0xFF)
        {
            reader.SkipDataItem();
            if (++count > cap) { return count; }
        }
        return !reader.Buffer.IsEmpty && reader.Buffer[0] == 0xFF ? count : -1;
    }

    /// <summary>Validates that constructor case bytes form exactly one complete CBOR item with zero trailing bytes.</summary>
    public static void ValidateCompleteCaseBytes(ReadOnlyMemory<byte> raw, string unionPath) =>
        _ = ValidateCompleteCore(raw, null, unionPath, asValidation: true);

    /// <summary>Validates an indexed <c>[CborList]</c> constructor case in one pass.</summary>
    public static void ValidateIndexedArrayCase(
        ReadOnlyMemory<byte> raw, int expectedIndex, int minItems, int maxItems, int? expectedTag, string unionPath,
        int secondaryDepth = -1, int secondaryMask = 0) =>
        _ = ValidateArrayCore(raw, null, expectedIndex, minItems, maxItems, expectedTag, unionPath, asValidation: true, secondaryDepth, secondaryMask);

    /// <summary>Validates an unindexed <c>[CborList]</c> constructor case in one pass.</summary>
    public static void ValidateArrayCase(ReadOnlyMemory<byte> raw, int minItems, int maxItems, int? expectedTag, string unionPath, int? outerTag = null) =>
        _ = ValidateArrayCore(raw, null, null, minItems, maxItems, expectedTag, unionPath, asValidation: true, outerTag: outerTag);

    /// <summary>Validates a tagged constructor case (exact or dynamic tag, optionally requiring an array payload).</summary>
    public static void ValidateTagCase(ReadOnlyMemory<byte> raw, int expectedTag, bool requireArray, string unionPath, int? outerTag = null) =>
        _ = ValidateTagCore(raw, null, expectedTag, requireArray, unionPath, asValidation: true, outerTag);

    /// <summary>Validates a container or map constructor case against its root CBOR major-type mask without skipping tags.</summary>
    public static void ValidateMajorTypeCase(ReadOnlyMemory<byte> raw, int expectedMajorMask, string unionPath) =>
        _ = ValidateMajorTypeCore(raw, null, expectedMajorMask, unionPath, asValidation: true);

    /// <summary>Validates that <typeparamref name="TCase"/> accepts <paramref name="raw"/> via its reader in full.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ValidateCaseRead<TCase>(ReadOnlyMemory<byte> raw, ReadWithConsumedHandler<TCase> reader, string unionPath)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(unionPath);
        try { _ = reader(raw, out int consumed); if (consumed != raw.Length) { throw CborError.Validation(Math.Max(0, consumed), unionPath, "complete CBOR union item", "mismatched item boundary"); } }
        catch (Exception error) when (CborError.IsDecodeFailure(error))
        {
            if (error is CborValidationException) { throw; }
            throw CborError.Validation(error is CborException cbor ? Math.Max(0, cbor.Offset) : error is CborDecodeException decoded ? decoded.Offset : -1, unionPath, "valid union case", error.Message, error);
        }
    }

    private static void CheckInput(ReadOnlyMemory<byte> data, int? expectedBytesConsumed, string unionPath, string nullExpected, bool asValidation)
    {
        ArgumentNullException.ThrowIfNull(unionPath);
        if (expectedBytesConsumed is int expected) { if (expected <= 0 || expected > data.Length) { throw ThrowInvalid(data, 0, unionPath, "registered union shape", "end of input", asValidation); } }
        else if (data.IsEmpty) { throw ThrowUninitialized(unionPath); }
        if (data.Span[0] is 0xF6 or 0xF7 or 0xFF) { throw ThrowInvalid(data, 0, unionPath, nullExpected, CborError.Actual(data, 0), asValidation); }
    }

    private static ReadOnlyMemory<byte> ValidateCompleteCore(ReadOnlyMemory<byte> data, int? expectedBytesConsumed, string unionPath, bool asValidation)
    {
        CheckInput(data, expectedBytesConsumed, unionPath, asValidation ? "non-null complete CBOR item" : "registered union shape", asValidation);
        int itemLength = MeasureCompleteItem(data, unionPath, asValidation);
        int target = expectedBytesConsumed ?? data.Length;
        if (itemLength != target) { throw ThrowInvalid(data, itemLength, unionPath, "complete CBOR item", asValidation ? "trailing bytes" : "mismatched item boundary", asValidation); }
        return data[..target];
    }

    private static void ReadExpectedTag(ref CborReader reader, ReadOnlyMemory<byte> data, int expectedTag, string unionPath, bool asValidation)
    {
        int offset = data.Length - reader.Buffer.Length;
        if (!reader.TryReadSemanticTag(out ulong actualTag)) { throw ThrowInvalid(data, offset, unionPath, expectedTag >= 0 ? $"tag {expectedTag}" : "semantic tag", CborError.Actual(data, offset), asValidation); }
        if (expectedTag >= 0 && actualTag != (ulong)expectedTag) { throw ThrowInvalid(data, offset, unionPath, $"tag {expectedTag}", $"tag {actualTag}", asValidation); }
    }

    private static ReadOnlyMemory<byte> ValidateTagCore(ReadOnlyMemory<byte> data, int? expectedBytesConsumed, int expectedTag, bool requireArray, string unionPath, bool asValidation, int? outerTag = null)
    {
        ReadOnlyMemory<byte> slice = ValidateCompleteCore(data, expectedBytesConsumed, unionPath, asValidation);
        try
        {
            CborReader reader = new(data.Span);
            if (outerTag is int outer) { ReadExpectedTag(ref reader, data, outer, unionPath, asValidation); }
            ReadExpectedTag(ref reader, data, expectedTag, unionPath, asValidation);
            if (requireArray && (reader.Buffer.IsEmpty || (reader.Buffer[0] >> 5) != 4)) { throw ThrowInvalid(data, data.Length - reader.Buffer.Length, unionPath, "array", CborError.Actual(data, data.Length - reader.Buffer.Length), asValidation); }
            return slice;
        }
        catch (CborException error) { throw ThrowWrapped(data, error, unionPath, asValidation); }
    }

    private static ReadOnlyMemory<byte> ValidateMajorTypeCore(ReadOnlyMemory<byte> data, int? expectedBytesConsumed, int expectedMajorMask, string unionPath, bool asValidation)
    {
        ReadOnlyMemory<byte> slice = ValidateCompleteCore(data, expectedBytesConsumed, unionPath, asValidation);
        byte initial = slice.Span[0];
        if (expectedMajorMask == BooleanMask ? initial is not (0xF4 or 0xF5) : (expectedMajorMask & (1 << (initial >> 5))) == 0)
        {
            throw ThrowInvalid(data, 0, unionPath, expectedMajorMask == BooleanMask ? "boolean" : "matching CBOR major type", CborError.Actual(data, 0), asValidation);
        }
        return slice;
    }

    private static int MeasureCompleteItem(ReadOnlyMemory<byte> data, string unionPath, bool asValidation)
    {
        try
        {
            CborReader reader = new(data.Span);
            reader.SkipDataItem();
            int itemLength = data.Length - reader.Buffer.Length;
            CborReader untag = new(data.Span[..itemLength]);
            while (!untag.Buffer.IsEmpty && (untag.Buffer[0] >> 5) == 6) { if (!untag.TryReadSemanticTag(out _)) { break; } }
            ReadOnlySpan<byte> untagged = untag.Buffer;
            if (untagged.IsEmpty || untagged[0] == 0xFF) { throw ThrowInvalid(data, 0, unionPath, "valid CBOR item", "break byte", asValidation); }
            byte head = untagged[0];
            if ((head >> 5) is >= 2 and <= 5 && (head & 0x1F) == 31 && (untagged.Length < 2 || untagged[^1] != 0xFF)) { throw ThrowInvalid(data, itemLength, unionPath, "terminated indefinite CBOR item", "end of input", asValidation); }
            return itemLength;
        }
        catch (CborException error) { throw ThrowWrapped(data, error, unionPath, asValidation); }
    }

    private static ReadOnlyMemory<byte> ValidateArrayCore(
        ReadOnlyMemory<byte> data, int? expectedBytesConsumed, int? expectedIndex, int minItems, int maxItems, int? expectedTag,
        string unionPath, bool asValidation, int secondaryDepth = -1, int secondaryMask = 0, int? outerTag = null)
    {
        CheckInput(data, expectedBytesConsumed, unionPath, "array", asValidation);
        try
        {
            CborReader reader = new(data.Span);
            if (outerTag is int outer) { ReadExpectedTag(ref reader, data, outer, unionPath, asValidation); }
            if (expectedTag is int tag) { ReadExpectedTag(ref reader, data, tag, unionPath, asValidation); }

            int arrayStart = data.Length - reader.Buffer.Length;
            if (reader.Buffer.IsEmpty || (reader.Buffer[0] >> 5) != 4) { throw ThrowInvalid(data, arrayStart, unionPath, "array", CborError.Actual(data, arrayStart), asValidation); }

            reader.ReadBeginArray();
            int count = reader.ReadSize();
            if (count >= 0)
            {
                if (count < minItems || count > maxItems || (expectedIndex.HasValue && count == 0) || (secondaryDepth >= 0 && count <= secondaryDepth)) { throw ThrowInvalid(data, arrayStart, unionPath, $"array length {minItems}..{maxItems}", $"array length {count}", asValidation); }
                for (int i = 0; i < count; i++) { ValidateArrayElement(ref reader, data, data.Length - reader.Buffer.Length, i, expectedIndex, secondaryDepth, secondaryMask, unionPath, asValidation); }
            }
            else
            {
                int actualCount = 0;
                while (!reader.Buffer.IsEmpty && reader.Buffer[0] != 0xFF)
                {
                    int elemOffset = data.Length - reader.Buffer.Length;
                    if (actualCount == maxItems) { throw ThrowInvalid(data, elemOffset, unionPath, $"at most {maxItems} items", "extra array item", asValidation); }
                    ValidateArrayElement(ref reader, data, elemOffset, actualCount++, expectedIndex, secondaryDepth, secondaryMask, unionPath, asValidation);
                }
                if (reader.Buffer.IsEmpty || reader.Buffer[0] != 0xFF) { throw ThrowInvalid(data, data.Length - reader.Buffer.Length, unionPath, "end of indefinite array", "end of input", asValidation); }
                _ = reader.ReadDataItem();
                if (actualCount < minItems || (expectedIndex.HasValue && actualCount == 0) || (secondaryDepth >= 0 && actualCount <= secondaryDepth)) { throw ThrowInvalid(data, arrayStart, unionPath, $"at least {minItems} items", $"array length {actualCount}", asValidation); }
            }

            int itemLength = data.Length - reader.Buffer.Length;
            int target = expectedBytesConsumed ?? data.Length;
            if (itemLength != target) { throw ThrowInvalid(data, itemLength, unionPath, "complete CBOR array", asValidation ? "trailing bytes" : "mismatched item boundary", asValidation); }
            return data[..target];
        }
        catch (CborException error) { throw ThrowWrapped(data, error, unionPath, asValidation); }
    }

    private static void ValidateArrayElement(
        ref CborReader reader, ReadOnlyMemory<byte> data, int offset, int index,
        int? expectedIndex, int secondaryDepth, int secondaryMask, string unionPath, bool asValidation)
    {
        if (index == 0 && expectedIndex is int idx) { ReadExpectedIndex(ref reader, data, offset, idx, unionPath, asValidation); return; }
        if (reader.Buffer.IsEmpty || reader.Buffer[0] == 0xFF) { throw ThrowInvalid(data, offset, unionPath, "array item", CborError.Actual(data, offset), asValidation); }
        if (index == secondaryDepth)
        {
            CborDataItemType t = new CborReader(reader.Buffer).GetCurrentDataItemType();
            int bit = t == CborDataItemType.Unsigned ? 1 << 0
                : t == CborDataItemType.Signed ? 1 << 1
                : t == CborDataItemType.ByteString ? 1 << 2
                : t == CborDataItemType.String ? 1 << 3
                : t == CborDataItemType.Array ? 1 << 4
                : t == CborDataItemType.Map ? 1 << 5
                : t == CborDataItemType.Boolean ? BooleanMask : 0;
            if ((secondaryMask & bit) == 0) { throw ThrowInvalid(data, offset, unionPath, "matching union discriminant", CborError.Actual(data, offset), asValidation); }
        }
        reader.SkipDataItem();
    }

    private static void ReadExpectedIndex(ref CborReader reader, ReadOnlyMemory<byte> data, int offset, int idx, string unionPath, bool asValidation)
    {
        if (reader.Buffer.IsEmpty || (reader.Buffer[0] >> 5) is not (0 or 1)) { throw ThrowInvalid(data, offset, unionPath, $"index {idx}", CborError.Actual(data, offset), asValidation); }
        int actualIdx = reader.ReadInt32();
        if (actualIdx != idx) { throw ThrowInvalid(data, offset, unionPath, $"index {idx}", actualIdx.ToString(CultureInfo.InvariantCulture), asValidation); }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception ThrowUninitialized(string unionPath) => CborError.Validation(-1, unionPath, "initialized union value", "default value");

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception ThrowInvalid(ReadOnlyMemory<byte> data, int offset, string unionPath, string expected, string actual, bool asValidation) =>
        asValidation ? CborError.Validation(offset, unionPath, expected, actual) : CborError.Decode(data, offset, unionPath, expected, actual);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Exception ThrowWrapped(ReadOnlyMemory<byte> data, CborException error, string unionPath, bool asValidation) =>
        asValidation
            ? CborError.Validation(Math.Max(0, error.Offset), unionPath, "valid CBOR union item", error.Message, error)
            : CborError.Wrap(error, data, unionPath, "registered union shape");
}
