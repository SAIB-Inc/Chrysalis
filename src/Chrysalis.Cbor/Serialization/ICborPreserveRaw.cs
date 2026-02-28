namespace Chrysalis.Cbor.Serialization;

/// <summary>
/// Interface indicating that the raw CBOR bytes should be preserved during deserialization.
/// </summary>
public interface ICborPreserveRaw
{
    /// <summary>
    /// Gets a value indicating whether raw CBOR bytes should be preserved.
    /// </summary>
    bool PreserveRaw => true;
}
