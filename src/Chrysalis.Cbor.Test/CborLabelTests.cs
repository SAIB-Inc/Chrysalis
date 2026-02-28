using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Test;

public class CborLabelTests
{
    [Fact]
    public void CborLabel_IntConstructor_Works()
    {
        // Arrange & Act
        CborLabel label = new(42);

        // Assert
        Assert.Equal(42, label.Value);
    }

    [Fact]
    public void CborLabel_LongConstructor_Works()
    {
        // Arrange & Act
        CborLabel label = new(42L);

        // Assert
        Assert.Equal(42L, label.Value);
    }

    [Fact]
    public void CborLabel_StringConstructor_Works()
    {
        // Arrange & Act
        CborLabel label = new("header");

        // Assert
        Assert.Equal("header", label.Value);
    }

    [Fact]
    public void CborLabel_StringConstructor_NullThrows()
    {
        // Act & Assert
        _ = Assert.Throws<ArgumentNullException>(() => new CborLabel(null!));
    }

    [Fact]
    public void CborLabel_ImplicitConversion_Int_Works()
    {
        // Arrange & Act
        CborLabel label = 42;

        // Assert
        Assert.Equal(42, label.Value);
    }

    [Fact]
    public void CborLabel_ImplicitConversion_Long_Works()
    {
        // Arrange & Act
        CborLabel label = 42L;

        // Assert
        Assert.Equal(42L, label.Value);
    }

    [Fact]
    public void CborLabel_ImplicitConversion_String_Works()
    {
        // Arrange & Act
        CborLabel label = "custom-header";

        // Assert
        Assert.Equal("custom-header", label.Value);
    }

    [Fact]
    public void CborLabel_IntValue_SerializesAsInteger()
    {
        // Arrange
        CborLabel label = 1;

        // Act
        byte[] bytes = CborSerializer.Serialize(label);
        string hex = Convert.ToHexString(bytes);

        // Assert
        Assert.Equal("01", hex); // CBOR integer 1
    }

    [Fact]
    public void CborLabel_StringValue_SerializesAsString()
    {
        // Arrange
        CborLabel label = "alg";

        // Act
        byte[] bytes = CborSerializer.Serialize(label);
        string hex = Convert.ToHexString(bytes);

        // Assert
        Assert.Equal("63616C67", hex); // CBOR text string "alg"
    }

    [Fact]
    public void CborLabel_IntValue_RoundTrip()
    {
        // Arrange
        CborLabel label = 42;

        // Act
        byte[] bytes = CborSerializer.Serialize(label);
        CborLabel deserialized = CborSerializer.Deserialize<CborLabel>(bytes);

        // Assert
        Assert.Equal(42L, deserialized.Value); // Note: deserializes as long
    }

    [Fact]
    public void CborLabel_StringValue_RoundTrip()
    {
        // Arrange
        CborLabel label = "custom-header";

        // Act
        byte[] bytes = CborSerializer.Serialize(label);
        CborLabel deserialized = CborSerializer.Deserialize<CborLabel>(bytes);

        // Assert
        Assert.Equal("custom-header", deserialized.Value);
    }

    [Fact]
    public void CborLabel_NegativeInteger_Works()
    {
        // Arrange
        CborLabel label = -1;

        // Act
        byte[] bytes = CborSerializer.Serialize(label);
        string hex = Convert.ToHexString(bytes);

        // Assert
        Assert.Equal("20", hex); // CBOR negative integer -1
    }

    [Fact]
    public void CborLabel_TypeSafety_InvalidTypesRejected()
    {
        // These should not compile due to type-safe constructors:
        // new CborLabel(3.14);       // Won't compile
        // new CborLabel(DateTime.Now); // Won't compile  
        // new CborLabel(new byte[]{}); // Won't compile

        // Only these compile:
        CborLabel intLabel = new(42);
        CborLabel longLabel = new(42L);
        CborLabel stringLabel = new("test");

        Assert.Equal(42, intLabel.Value);
        Assert.Equal(42L, longLabel.Value);
        Assert.Equal("test", stringLabel.Value);
    }
}