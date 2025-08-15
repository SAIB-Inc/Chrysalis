using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Xunit;

namespace Chrysalis.Cbor.Test;

public class CborLabelTests
{
    [Fact]
    public void CborLabel_IntegerValue_SerializesAsInteger()
    {
        // Arrange
        var label = new CborLabel(1);
        
        // Act
        var bytes = CborSerializer.Serialize(label);
        var hex = Convert.ToHexString(bytes);
        
        // Assert
        Assert.Equal("01", hex); // CBOR integer 1
    }
    
    [Fact]
    public void CborLabel_StringValue_SerializesAsString()
    {
        // Arrange
        var label = new CborLabel("alg");
        
        // Act
        var bytes = CborSerializer.Serialize(label);
        var hex = Convert.ToHexString(bytes);
        
        // Assert
        Assert.Equal("63616C67", hex); // CBOR text string "alg" (63 = 3-byte string, 616C67 = "alg")
    }
    
    [Fact]
    public void CborLabel_IntegerValue_RoundTrip()
    {
        // Arrange
        var label = new CborLabel(42);
        
        // Act
        var bytes = CborSerializer.Serialize(label);
        var deserialized = CborSerializer.Deserialize<CborLabel>(bytes);
        
        // Assert
        Assert.Equal(42L, deserialized.Value); // Note: deserializes as long
    }
    
    [Fact]
    public void CborLabel_StringValue_RoundTrip()
    {
        // Arrange
        var label = new CborLabel("custom-header");
        
        // Act
        var bytes = CborSerializer.Serialize(label);
        var deserialized = CborSerializer.Deserialize<CborLabel>(bytes);
        
        // Assert
        Assert.Equal("custom-header", deserialized.Value);
    }
    
    [Fact]
    public void CborLabel_NegativeInteger_Works()
    {
        // Arrange
        var label = new CborLabel(-1);
        
        // Act
        var bytes = CborSerializer.Serialize(label);
        var hex = Convert.ToHexString(bytes);
        
        // Assert
        Assert.Equal("20", hex); // CBOR negative integer -1 (encoded as 0)
    }
}