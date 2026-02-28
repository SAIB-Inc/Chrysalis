using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Chrysalis.Wallet.CIPs.CIP8.Models;

namespace Chrysalis.Wallet.Test.CIPs.CIP8;

public class HeaderMapTests
{
    [Fact]
    public void HeaderMap_WithHashed_Works()
    {
        // Arrange & Act
        HeaderMap headerMap = HeaderMap.WithHashed(true);

        // Assert
        _ = Assert.Single(headerMap.Headers);
        Assert.True(headerMap.Headers.ContainsKey("hashed"));

        CborBool boolValue = (CborBool)headerMap.Headers["hashed"];
        Assert.True(boolValue.Value);
    }

    [Fact]
    public void HeaderMap_WithHeader_IntKey_Works()
    {
        // Arrange
        HeaderMap headerMap = HeaderMap.Empty;

        // Act
        HeaderMap result = headerMap.WithHeader(1, -7); // Algorithm: ES256

        // Assert
        _ = Assert.Single(result.Headers);
        Assert.True(result.Headers.ContainsKey(1));

        CborInt intValue = (CborInt)result.Headers[1];
        Assert.Equal(-7, intValue.Value);
    }

    [Fact]
    public void HeaderMap_WithHeader_StringKey_Works()
    {
        // Arrange
        HeaderMap headerMap = HeaderMap.Empty;

        // Act  
        HeaderMap result = headerMap.WithHeader("custom", "value");

        // Assert
        _ = Assert.Single(result.Headers);
        Assert.True(result.Headers.ContainsKey("custom"));

        CborString stringValue = (CborString)result.Headers["custom"];
        Assert.Equal("value", stringValue.Value);
    }

    [Fact]
    public void HeaderMap_COSEPattern_Works()
    {
        // Arrange & Act - Real-world COSE header usage
        HeaderMap headers = HeaderMap.Empty
            .WithHeader(1, -7)                    // Algorithm: ES256 
            .WithHeader(4, "my-key-id")          // Key ID
            .WithHeader("iss", "https://issuer") // Custom issuer
            .WithHeader("hashed", true);         // Custom hashed flag

        // Assert
        Assert.Equal(4, headers.Headers.Count);

        // Check algorithm
        CborInt algorithm = (CborInt)headers.Headers[1];
        Assert.Equal(-7, algorithm.Value);

        // Check key ID
        CborString keyId = (CborString)headers.Headers[4];
        Assert.Equal("my-key-id", keyId.Value);

        // Check custom issuer
        CborString issuer = (CborString)headers.Headers["iss"];
        Assert.Equal("https://issuer", issuer.Value);

        // Check hashed flag
        CborBool hashed = (CborBool)headers.Headers["hashed"];
        Assert.True(hashed.Value);
    }

    [Fact]
    public void HeaderMap_Serialization_RoundTrip()
    {
        // Arrange
        HeaderMap original = HeaderMap.Empty
            .WithHeader(1, -7)
            .WithHeader("custom", "test");

        // Act
        byte[] bytes = CborSerializer.Serialize(original);
        HeaderMap deserialized = CborSerializer.Deserialize<HeaderMap>(bytes);

        // Assert
        Assert.Equal(2, deserialized.Headers.Count);

        // Note: CBOR deserialization may convert int to long

        // Check that we can find the keys by iterating
        bool foundIntKey = false;
        bool foundStringKey = false;

        foreach ((CborLabel key, CborPrimitive value) in deserialized.Headers)
        {
            switch (key.Value)
            {
                case long l when l == 1:
                case int i when i == 1:
                    foundIntKey = true;
                    CborInt algorithm = (CborInt)value;
                    Assert.Equal(-7, algorithm.Value);
                    break;
                case string s when s == "custom":
                    foundStringKey = true;
                    CborString custom = (CborString)value;
                    Assert.Equal("test", custom.Value);
                    break;
                default:
                    break;
            }
        }

        Assert.True(foundIntKey, "Should find integer key");
        Assert.True(foundStringKey, "Should find string key");
    }
}