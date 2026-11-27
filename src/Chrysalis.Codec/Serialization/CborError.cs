using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Serialization.Attributes;
using Chrysalis.Codec.Types.Cardano.Core;
using SAIB.Cbor;

namespace Chrysalis.Codec.Serialization;

/// <summary>Failure-only helpers shared by generated codecs and runtime dispatch.</summary>
public static class CborError
{
    /// <summary>Creates an error without allocating diagnostic text on the successful path.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Exception Decode(ReadOnlyMemory<byte> data, int offset, string typePath, string expected, object? actual = null) =>
        new CborDecodeException(offset, typePath, expected, actual?.ToString() ?? Actual(data, offset));

    /// <summary>Creates a shape error whose type-based expectation is resolved only when observed.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Exception Decode(ReadOnlyMemory<byte> data, int offset, string typePath, Type expected) =>
        new CborDecodeException(offset, typePath, expected, Actual(data, offset));

    /// <summary>Creates a tag error without boxing or formatting the tag on a discarded trial.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Exception DecodeTag(string typePath, string expected, ulong actual) =>
        new CborDecodeException(typePath, expected, actual);

    /// <summary>Creates a validation failure without loading its exception type on successful reads.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Exception Validation(int offset, string typePath, string expected, string actual, Exception? innerException = null) =>
        new CborValidationException(offset, typePath, expected, actual, innerException);

    /// <summary>Translates reader and dispatch failures, retaining structured errors and their causes.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Exception Wrap(Exception error, ReadOnlyMemory<byte> data, string typePath, string expected, int offset = 0)
    {
        ArgumentNullException.ThrowIfNull(typePath);
        while (error is TargetInvocationException { InnerException: not null } invocation)
        {
            error = invocation.InnerException!;
        }
        if (!IsDecodeFailure(error))
        {
            ExceptionDispatchInfo.Throw(error);
        }
        if (error is UnknownEraException unknown)
        {
            return unknown;
        }
        if (error is CborDecodeException decoded)
        {
            if (offset == 0 && (decoded.TypePath == typePath || (decoded.TypePath.Length > typePath.Length && decoded.TypePath[typePath.Length] == '.'
                && decoded.TypePath.StartsWith(typePath, StringComparison.Ordinal))))
            {
                return decoded;
            }
            string path = typePath + "." + decoded.TypePath;
            int position = decoded.Offset < 0 ? -1 : offset + decoded.Offset;
            return decoded is CborValidationException
                ? new CborValidationException(position, path, decoded.Expected, decoded.Actual, decoded)
                : new CborDecodeException(position, path, decoded.Expected, decoded.Actual, decoded);
        }
        return error is CborException cbor
            ? new CborDecodeException(offset + Math.Max(0, cbor.Offset), typePath, expected, Observed(data, cbor), cbor)
            : new CborDecodeException(offset, typePath, expected, Actual(data, 0), error);
    }

    /// <summary>Adds lazy-property context; offsets are relative to that property's owning value.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Exception Field(Exception error, ReadOnlyMemory<byte> raw, ReadOnlyMemory<byte> field, string typePath, string expected) =>
        Wrap(error, field, typePath, expected, field.IsEmpty ? raw.Length : raw.Span.Overlaps(field.Span, out int offset) ? offset : 0);

    /// <summary>Adds a field position. Sliced children and direct reader calls have different origins.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Exception Reader(Exception error, ReadOnlyMemory<byte> data, int remainingLength, int itemStart, bool sliced, string typePath, string expected)
    {
        if (error is CborDecodeException decoded && decoded.TypePath == typePath)
        {
            return decoded;
        }
        int origin = error is CborException cbor && !sliced ? ReaderOrigin(data, remainingLength, cbor) : itemStart;
        return Wrap(error, data[origin..], typePath, expected, origin);
    }

    private static int ReaderOrigin(ReadOnlyMemory<byte> data, int remainingLength, CborException error) =>
        Math.Clamp(data.Length - remainingLength - Math.Max(0, error.Offset), 0, data.Length);

    // Generated positional readers attach context in a false filter during union trials.
    // Retain only the innermost field and absolute position: discarded misses get no
    // wrapper, Exception.Data, path concatenation, or second throw.
    [ThreadStatic]
    private static CborReadTrial _trial;

    /// <summary>Begins a union trial, preserving any enclosing trial.</summary>
    public static CborReadTrial BeginReadTrial()
    {
        CborReadTrial previous = _trial;
        _trial = new CborReadTrial { Active = true };
        return previous;
    }

    /// <summary>Takes the candidate's recorded context and clears it before the next candidate.</summary>
    public static CborReadTrial TakeReadTrial(Exception error)
    {
        CborReadTrial current = _trial;
        _trial = new CborReadTrial { Active = true };
        return ReferenceEquals(current.Error, error) ? current : default;
    }

    /// <summary>Restores the enclosing trial, including on successful or interrupted reads.</summary>
    public static void EndReadTrial(CborReadTrial previous) => _trial = previous;

    /// <summary>Wraps outside a trial; inside a trial records the context and lets the original throw continue.</summary>
    public static bool ShouldWrapReader(Exception error, ReadOnlyMemory<byte> data, int remainingLength, int itemStart, bool sliced, string typePath, Type expected)
    {
        if (!IsDecodeFailure(error))
        {
            return false;
        }
        if (!_trial.Active)
        {
            return true;
        }
        if (ReferenceEquals(_trial.Error, error))
        {
            if (sliced && _trial.Offset >= 0)
            {
                _trial.Offset += itemStart;
            }
            return false;
        }

        _trial.Error = error;
        _trial.Path = typePath;
        _trial.Expected = expected;
        if (error is CborException cbor)
        {
            int origin = sliced ? itemStart : ReaderOrigin(data, remainingLength, cbor);
            _trial.Offset = origin + Math.Max(0, cbor.Offset);
            _trial.Actual = Observed(data[origin..], cbor);
        }
        else
        {
            _trial.Offset = error is CborDecodeException typed
                ? sliced && typed.Offset >= 0 ? itemStart + typed.Offset : typed.Offset
                : itemStart;
            _trial.Actual = Actual(data, itemStart);
        }
        return false;
    }

    /// <summary>Constructs the final typed error if all union candidates failed.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static Exception FinishReadTrial(Exception? error, CborReadTrial failure, ReadOnlyMemory<byte> data, string typePath)
    {
        const string expected = "registered union case";
        if (error is null)
        {
            return Decode(data, 0, typePath, expected);
        }
        if (failure.Path is null || error is UnknownEraException)
        {
            return Wrap(error, data, typePath, expected);
        }
        string path = typePath + "." + failure.Path;
        if (error is CborDecodeException typed)
        {
            if (typed.TypePath != failure.Path)
            {
                path += "." + typed.TypePath;
            }
            return typed is CborValidationException
                ? new CborValidationException(failure.Offset, path, typed.Expected, typed.Actual, typed)
                : new CborDecodeException(failure.Offset, path, typed.Expected, typed.Actual, typed);
        }
        return new CborDecodeException(failure.Offset, path, Expected(failure.Expected!), failure.Actual!, error);
    }

    /// <summary>Identifies input and codec failures, not resource exhaustion, cancellation, or type initialization failures.</summary>
    public static bool IsDecodeFailure(Exception error) => error is not (OutOfMemoryException or OperationCanceledException
        or TimeoutException or StackOverflowException or AccessViolationException or ThreadInterruptedException
        or IOException or TypeInitializationException);

    // SAIB 3.1 reports its cursor. Expect has already consumed the failing header;
    // an unsuccessful payload read, by contrast, has no observed byte even if some payload remains.
    private static string Observed(ReadOnlyMemory<byte> data, CborException error)
    {
        ReadOnlySpan<char> reason = error.Message.AsSpan();
        reason = reason[(reason.IndexOf(']') + 1)..].TrimStart();
        if (reason.SequenceEqual("Unexpected end of buffer"))
        {
            return "end of input";
        }
        // SAIB's numeric text conversions consume the entire string before parsing it.
        if (reason.StartsWith("Cannot parse ", StringComparison.Ordinal))
        {
            return "text string";
        }
        int offset = Math.Max(0, error.Offset);
        if (offset > 0 && (reason.StartsWith("Expected major type ", StringComparison.Ordinal)
            || reason.StartsWith("Expected primitive ", StringComparison.Ordinal)
            || reason.StartsWith("Invalid major type ", StringComparison.Ordinal)
            || reason.StartsWith("Invalid primitive ", StringComparison.Ordinal)))
        {
            offset--;
        }
        return Actual(data, offset);
    }

    /// <summary>Names the wire major type at an offset without asking the reader to consume it.</summary>
    public static string Actual(ReadOnlyMemory<byte> data, int offset) => (uint)offset >= (uint)data.Length ? "end of input" : (data.Span[offset] >> 5) switch
    {
        0 => "unsigned integer",
        1 => "negative integer",
        2 => "byte string",
        3 => "text string",
        4 => "array",
        5 => "map",
        6 => "semantic tag",
        _ => "simple value or floating point"
    };

    /// <summary>Names the primitive wire expectation or the requested codec type.</summary>
    public static string Expected(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.IsDefined(typeof(CborConstrAttribute), inherit: false) || type.IsDefined(typeof(CborTagAttribute), inherit: false)) { return "semantic tag"; }
        if (type.IsDefined(typeof(CborListAttribute), inherit: false)) { return "array"; }
        if (type.IsDefined(typeof(CborMapAttribute), inherit: false)) { return "map"; }
        if (type.IsGenericType)
        {
            Type definition = type.GetGenericTypeDefinition();
            if (definition == typeof(List<>) || definition == typeof(CborList<>)) { return "array"; }
            if (definition == typeof(Dictionary<,>) || definition == typeof(CborMap<,>)) { return "map"; }
        }
        string Name() => type.IsGenericType ? type.ToString() : type.FullName ?? type.Name;
        return Type.GetTypeCode(Nullable.GetUnderlyingType(type) ?? type) switch
        {
            TypeCode.Int32 or TypeCode.Int64 or TypeCode.Int16 or TypeCode.SByte => "integer",
            TypeCode.UInt32 or TypeCode.UInt64 or TypeCode.UInt16 or TypeCode.Byte => "unsigned integer",
            TypeCode.String or TypeCode.Char => "text string",
            TypeCode.Boolean => "boolean",
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => "floating point",
            TypeCode.Empty or TypeCode.Object or TypeCode.DBNull or TypeCode.DateTime =>
                type == typeof(byte[]) || type == typeof(ReadOnlyMemory<byte>) ? "byte string" : Name(),
            _ => Name()
        };
    }
}

/// <summary>Scoped, synchronous error context for a generated union fallback.</summary>
public record struct CborReadTrial
{
    internal bool Active { get; set; }
    internal Exception? Error { get; set; }
    internal string? Path { get; set; }
    internal Type? Expected { get; set; }
    internal int Offset { get; set; }
    internal string? Actual { get; set; }
}
