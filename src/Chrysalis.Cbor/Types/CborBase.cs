namespace Chrysalis.Cbor.Types;

/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
/// <summary>
/// Base class for all CBOR-serializable types
/// </summary>
public abstract partial record CborBase<T> where T : CborBase<T>
{
    // Raw serialized data for caching and quick re-serialization
    public ReadOnlyMemory<byte>? Raw { get; set; }

    // Type discriminator for unions (to avoid reflection)
    public string? CborTypeName { get; set; }

    public static T Read(ReadOnlyMemory<byte> data)
    {
        throw new NotImplementedException("Read method not implemented for " + typeof(T).Name);
    }

    public static ReadOnlyMemory<byte> Write(T value)
    {
        throw new NotImplementedException("Write method not implemented for " + typeof(T).Name);
    }
}