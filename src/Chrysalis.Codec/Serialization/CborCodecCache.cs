using System.Buffers;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Chrysalis.Codec.Types;
using Chrysalis.Codec.Types.Cardano.Core.Common;
using SAIB.Cbor.Serialization;

namespace Chrysalis.Codec.Serialization;

/// <summary>Registers generated CBOR serializers for an open-generic type.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface ICborCodecRegistrar
{
    /// <summary>Registers this type's generated reader and writer with <see cref="CborCodecCache"/>.</summary>
    void RegisterCodec();
}

internal delegate object? BoxedReadWithConsumedHandler(ReadOnlyMemory<byte> data, out int bytesConsumed);

/// <summary>Thread-safe registry of typed CBOR readers and writers.</summary>
public static class CborCodecCache
{
    private static readonly ConcurrentDictionary<Type, BoxedReadWithConsumedHandler> BoxedReaders = [];

    static CborCodecCache()
    {
        RegisterStruct(CborPrimitiveReaders.ReadBoolean, CborPrimitiveWriters.WriteBoolean);
        RegisterStruct(CborPrimitiveReaders.ReadInt32, CborPrimitiveWriters.WriteInt32);
        RegisterStruct(CborPrimitiveReaders.ReadUInt32, CborPrimitiveWriters.WriteUInt32);
        RegisterStruct(CborPrimitiveReaders.ReadInt64, CborPrimitiveWriters.WriteInt64);
        RegisterStruct(CborPrimitiveReaders.ReadUInt64, CborPrimitiveWriters.WriteUInt64);
        RegisterStruct(CborPrimitiveReaders.ReadSingle, CborPrimitiveWriters.WriteSingle);
        RegisterStruct(CborPrimitiveReaders.ReadDouble, CborPrimitiveWriters.WriteDouble);
        RegisterStruct(CborPrimitiveReaders.ReadDecimal, CborPrimitiveWriters.WriteDecimal);
        Register(CborPrimitiveReaders.ReadString, CborPrimitiveWriters.WriteString);
        Register(CborPrimitiveReaders.ReadByteArray, CborPrimitiveWriters.WriteByteArray);
        RegisterStruct(CborPrimitiveReaders.ReadByteMemory, CborPrimitiveWriters.WriteByteMemory);
        RegisterStruct(CborEncodedValue.Read, CborEncodedValue.Write);
        Register(CborPrimitiveReaders.ReadCborAny, CborPrimitiveWriters.WriteCborAny);
        RegisterStruct(CborPrimitiveReaders.ReadCborLabel, CborPrimitiveWriters.WriteCborLabel);
        RegisterStruct(Address.Read, Address.Write);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void EnsureBuiltInsInitialized() => _ = BoxedReaders;

    /// <summary>Gets the registered reader delegate for <typeparamref name="T"/>, or <see langword="null"/> if unregistered.</summary>
    public static ReadWithConsumedHandler<T>? GetReader<T>() => CborCodecCache<T>.Reader;

    /// <summary>Gets the registered writer delegate for <typeparamref name="T"/>, or <see langword="null"/> if unregistered.</summary>
    public static WriteHandler<T>? GetWriter<T>() => CborCodecCache<T>.Writer;

    /// <summary>Registers reader and writer delegates for <typeparamref name="T"/>.</summary>
    public static void Register<T>(ReadWithConsumedHandler<T> reader, WriteHandler<T> writer)
    {
        ArgumentNullException.ThrowIfNull(reader); ArgumentNullException.ThrowIfNull(writer);
        CborCodecCache<T>.Set(reader, writer);
        _ = BoxedReaders.TryAdd(typeof(T), BoxedReaderAdapter<T>.Read);
    }

    /// <summary>Registers reader and writer delegates for value type <typeparamref name="T"/> and <see cref="Nullable{T}"/>.</summary>
    public static void RegisterStruct<T>(ReadWithConsumedHandler<T> reader, WriteHandler<T> writer) where T : struct
    {
        ArgumentNullException.ThrowIfNull(reader); ArgumentNullException.ThrowIfNull(writer);
        CborCodecCache<T>.Set(reader, writer);
        _ = BoxedReaders.TryAdd(typeof(T), BoxedReaderAdapter<T>.Read);
        CborCodecCache<T?>.Set(NullableCodecAdapter<T>.Read, NullableCodecAdapter<T>.Write);
    }

    /// <summary>Registers a reader delegate for <typeparamref name="T"/>.</summary>
    public static void RegisterReader<T>(ReadWithConsumedHandler<T> reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        CborCodecCache<T>.SetReader(reader);
        _ = BoxedReaders.TryAdd(typeof(T), BoxedReaderAdapter<T>.Read);
    }

    /// <summary>Registers a reader delegate for value type <typeparamref name="T"/> and <see cref="Nullable{T}"/>.</summary>
    public static void RegisterStructReader<T>(ReadWithConsumedHandler<T> reader) where T : struct
    {
        ArgumentNullException.ThrowIfNull(reader);
        CborCodecCache<T>.SetReader(reader);
        _ = BoxedReaders.TryAdd(typeof(T), BoxedReaderAdapter<T>.Read);
        CborCodecCache<T?>.SetReader(NullableCodecAdapter<T>.Read);
    }

    /// <summary>Registers a writer delegate for <typeparamref name="T"/>.</summary>
    public static void RegisterWriter<T>(WriteHandler<T> writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        CborCodecCache<T>.SetWriter(writer);
    }

    /// <summary>Registers a writer delegate for value type <typeparamref name="T"/> and <see cref="Nullable{T}"/>.</summary>
    public static void RegisterStructWriter<T>(WriteHandler<T> writer) where T : struct
    {
        ArgumentNullException.ThrowIfNull(writer);
        CborCodecCache<T>.SetWriter(writer);
        CborCodecCache<T?>.SetWriter(NullableCodecAdapter<T>.Write);
    }

    internal static BoxedReadWithConsumedHandler GetBoxedReaderOrThrow(Type type, ReadOnlyMemory<byte> data)
    {
        ArgumentNullException.ThrowIfNull(type);
        Type key = Nullable.GetUnderlyingType(type) ?? type;
        if (!BoxedReaders.TryGetValue(key, out BoxedReadWithConsumedHandler? reader))
        {
            EnsureModuleInitialized(key, 0, default);
            if (!BoxedReaders.TryGetValue(key, out reader))
            {
                throw CborError.Decode(data, 0, type.FullName ?? type.Name, "registered CBOR type", "unregistered type");
            }
        }
        return reader;
    }

    internal static void EnsureModuleInitialized(Type type, int depth, ModuleHandle lastHandle)
    {
        Type target = Nullable.GetUnderlyingType(type) ?? type;
        while (target.HasElementType) { target = target.GetElementType()!; }
        ModuleHandle nextHandle = lastHandle;
        try
        {
            ModuleHandle handle = target.Module.ModuleHandle;
            if (handle != ModuleHandle.EmptyHandle && handle != lastHandle) { RuntimeHelpers.RunModuleConstructor(handle); }
            nextHandle = handle;
        }
        catch (PlatformNotSupportedException) { }
        if (depth < 16)
        {
            foreach (Type arg in target.GenericTypeArguments) { EnsureModuleInitialized(arg, depth + 1, nextHandle); }
        }
    }

    private static class BoxedReaderAdapter<T>
    {
        internal static object? Read(ReadOnlyMemory<byte> data, out int bytesConsumed) =>
            CborCodecCache<T>.GetReaderOrThrow(data)(data, out bytesConsumed);
    }

    private static class NullableCodecAdapter<T> where T : struct
    {
        internal static T? Read(ReadOnlyMemory<byte> data, out int bytesConsumed)
        {
            try { return CborCodecCache<T>.GetReaderOrThrow(data)(data, out bytesConsumed); }
            catch (Exception error) when (CborError.IsDecodeFailure(error))
            { throw CborError.Wrap(error, data, typeof(T?).FullName!, CborError.Expected(typeof(T?))); }
        }

        internal static void Write(IBufferWriter<byte> output, T? value)
        {
            if (!value.HasValue) { throw CborError.Validation(-1, typeof(T?).FullName!, "non-null value", "null"); }
            CborCodecCache<T>.GetWriterOrThrow(value.Value)(output, value.Value);
        }
    }
}

internal static class CborCodecCache<T>
{
    private static ReadWithConsumedHandler<T>? _reader;
    private static WriteHandler<T>? _writer;

    internal static ReadWithConsumedHandler<T>? Reader => Volatile.Read(ref _reader);
    internal static WriteHandler<T>? Writer => Volatile.Read(ref _writer);

    internal static void Set(ReadWithConsumedHandler<T> reader, WriteHandler<T> writer)
    { _ = Interlocked.CompareExchange(ref _reader, reader, null); _ = Interlocked.CompareExchange(ref _writer, writer, null); }
    internal static void SetReader(ReadWithConsumedHandler<T> reader) => _ = Interlocked.CompareExchange(ref _reader, reader, null);
    internal static void SetWriter(WriteHandler<T> writer) => _ = Interlocked.CompareExchange(ref _writer, writer, null);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ReadWithConsumedHandler<T> GetReaderOrThrow(ReadOnlyMemory<byte> data) =>
        Volatile.Read(ref _reader) ?? ResolveReaderOrThrow(data);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static WriteHandler<T> GetWriterOrThrow(T value) =>
        Volatile.Read(ref _writer) ?? ResolveWriterOrThrow(value);

    private static void TryRegister(T? instance)
    {
        if (instance is ICborCodecRegistrar registrar)
        {
            registrar.RegisterCodec();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ReadWithConsumedHandler<T> ResolveReaderOrThrow(ReadOnlyMemory<byte> data)
    {
        CborCodecCache.EnsureBuiltInsInitialized();
        if (Volatile.Read(ref _reader) is { } builtIn) { return builtIn; }
        TryRegister(default);
        if (Volatile.Read(ref _reader) is { } registered) { return registered; }
        CborCodecCache.EnsureModuleInitialized(typeof(T), 0, default);
        return Volatile.Read(ref _reader)
            ?? throw CborError.Decode(data, 0, typeof(T).FullName ?? typeof(T).Name, "registered CBOR type", "unregistered type");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WriteHandler<T> ResolveWriterOrThrow(T value)
    {
        CborCodecCache.EnsureBuiltInsInitialized();
        if (Volatile.Read(ref _writer) is { } builtIn) { return builtIn; }
        TryRegister(value);
        if (Volatile.Read(ref _writer) is { } registered) { return registered; }
        CborCodecCache.EnsureModuleInitialized(typeof(T), 0, default);
        return Volatile.Read(ref _writer)
            ?? throw CborError.Validation(-1, typeof(T).FullName ?? typeof(T).Name, "registered CBOR type", "unregistered type");
    }
}

/// <summary>Pre-built reader methods for primitive CBOR types.</summary>
public static class CborPrimitiveReaders
{
    public static int ReadInt32(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); int v = r.ReadInt32(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(int).FullName!, "integer"); }
    }

    public static uint ReadUInt32(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); uint v = r.ReadUInt32(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(uint).FullName!, "unsigned integer"); }
    }

    public static long ReadInt64(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); long v = r.ReadInt64(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(long).FullName!, "integer"); }
    }

    public static ulong ReadUInt64(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); ulong v = r.ReadUInt64(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(ulong).FullName!, "unsigned integer"); }
    }

    public static bool ReadBoolean(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); bool v = r.ReadBoolean(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(bool).FullName!, "boolean"); }
    }

    public static string ReadString(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); string v = r.ReadString()!; bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(string).FullName!, "text string"); }
    }

    public static float ReadSingle(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); float v = r.ReadSingle(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(float).FullName!, "floating point"); }
    }

    public static double ReadDouble(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); double v = r.ReadDouble(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(double).FullName!, "floating point"); }
    }

    public static byte[] ReadByteArray(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); byte[] v = r.ReadByteStringToArray(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(byte[]).FullName!, "byte string"); }
    }

    public static ReadOnlyMemory<byte> ReadByteMemory(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try
        {
            CborReader reader = new(data.Span);
            ReadOnlySpan<byte> content = reader.Buffer;
            if (!content.IsEmpty && (content[0] >> 5) == 6) { CborReader probe = reader; _ = probe.TryReadSemanticTag(out _); content = probe.Buffer; }
            ReadOnlyMemory<byte> value;
            if (!content.IsEmpty && content[0] == 0x5F) { value = reader.ReadByteStringToArray(); }
            else { CborReader sr = new(new ReadOnlySequence<byte>(data)); value = sr.ReadByteStringSequence().First; reader = new(data.Span[sr.GetBookmark().currentPos..]); }
            bytesConsumed = data.Length - reader.Buffer.Length;
            return value;
        }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(ReadOnlyMemory<byte>).FullName!, "byte string"); }
    }

    internal static decimal ReadDecimal(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try { CborReader r = new(data.Span); decimal v = r.ReadDecimal(); bytesConsumed = data.Length - r.Buffer.Length; return v; }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(decimal).FullName!, "floating point"); }
    }

    internal static CborAny ReadCborAny(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try
        {
            CborReader r = new(data.Span);
            if (!r.Buffer.IsEmpty && r.Buffer[0] == 0xFF) { throw CborError.Decode(data, 0, typeof(CborAny).FullName!, "valid CBOR data item", "unexpected break"); }
            _ = r.ReadDataItem();
            bytesConsumed = data.Length - r.Buffer.Length;
            return new CborAny(data[..bytesConsumed]);
        }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(CborAny).FullName!, "valid CBOR data item"); }
    }

    internal static CborLabel ReadCborLabel(ReadOnlyMemory<byte> data, out int bytesConsumed)
    {
        try
        {
            CborReader r = new(data.Span);
            CborDataItemType itemType = r.GetCurrentDataItemType();
            CborLabel label;
            if (itemType is CborDataItemType.Unsigned or CborDataItemType.Signed) { label = new CborLabel(r.ReadInt64()); }
            else if (itemType == CborDataItemType.String) { label = new CborLabel(r.ReadString()!); }
            else { throw CborError.Decode(data, 0, typeof(CborLabel).FullName!, "integer or text string"); }
            bytesConsumed = data.Length - r.Buffer.Length;
            return label;
        }
        catch (Exception e) when (CborError.IsDecodeFailure(e)) { throw CborError.Wrap(e, data, typeof(CborLabel).FullName!, "integer or text string"); }
    }
}

internal static class CborPrimitiveWriters
{
    internal static void WriteBoolean(IBufferWriter<byte> output, bool value) => new CborWriter(output).WriteBoolean(value);
    internal static void WriteInt32(IBufferWriter<byte> output, int value) => new CborWriter(output).WriteInt32(value);
    internal static void WriteUInt32(IBufferWriter<byte> output, uint value) => new CborWriter(output).WriteUInt32(value);
    internal static void WriteInt64(IBufferWriter<byte> output, long value) => new CborWriter(output).WriteInt64(value);
    internal static void WriteUInt64(IBufferWriter<byte> output, ulong value) => new CborWriter(output).WriteUInt64(value);
    internal static void WriteSingle(IBufferWriter<byte> output, float value) => new CborWriter(output).WriteSingle(value);
    internal static void WriteDouble(IBufferWriter<byte> output, double value) => new CborWriter(output).WriteDouble(value);
    internal static void WriteDecimal(IBufferWriter<byte> output, decimal value) => new CborWriter(output).WriteDecimal(value);
    internal static void WriteString(IBufferWriter<byte> output, string value) { ArgumentNullException.ThrowIfNull(value); new CborWriter(output).WriteString(value); }
    internal static void WriteByteArray(IBufferWriter<byte> output, byte[] value) { ArgumentNullException.ThrowIfNull(value); new CborWriter(output).WriteByteString(value); }
    internal static void WriteByteMemory(IBufferWriter<byte> output, ReadOnlyMemory<byte> value) => new CborWriter(output).WriteByteString(value.Span);

    internal static void WriteCborAny(IBufferWriter<byte> output, CborAny value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ReadOnlySpan<byte> raw = value.Raw.Span;
        if (raw.IsEmpty) { throw CborError.Validation(-1, typeof(CborAny).FullName!, "valid CBOR data item", "empty"); }
        if (raw[0] == 0xFF) { throw CborError.Validation(-1, typeof(CborAny).FullName!, "valid CBOR data item", "unexpected break"); }
        try { CborReader r = new(raw); _ = r.ReadDataItem(); if (!r.Buffer.IsEmpty) { throw CborError.Validation(-1, typeof(CborAny).FullName!, "single CBOR data item", "trailing bytes"); } }
        catch (Exception ex) when (ex is not CborValidationException && CborError.IsDecodeFailure(ex)) { throw CborError.Validation(-1, typeof(CborAny).FullName!, "valid CBOR data item", ex.Message, ex); }
        output.Write(raw);
    }

    internal static void WriteCborLabel(IBufferWriter<byte> output, CborLabel value)
    {
        CborWriter writer = new(output);
        switch (value.Value)
        {
            case int i: writer.WriteInt32(i); break;
            case long l: writer.WriteInt64(l); break;
            case string s: writer.WriteString(s); break;
            default: throw new CborValidationException(-1, "CborLabel", "integer or text string", value.Value?.GetType().FullName ?? "null");
        }
    }
}
