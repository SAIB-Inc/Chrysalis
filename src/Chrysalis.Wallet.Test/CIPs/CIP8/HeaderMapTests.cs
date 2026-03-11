using Chrysalis.Codec.Serialization;
using Chrysalis.Codec.Types;
using Chrysalis.Wallet.CIPs.CIP8.Models;
using SAIB.Cbor.Serialization;

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

        bool boolValue = DecodeBool(headerMap.Headers["hashed"]);
        Assert.True(boolValue);
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

        int intValue = DecodeInt(result.Headers[1]);
        Assert.Equal(-7, intValue);
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

        string stringValue = DecodeString(result.Headers["custom"]);
        Assert.Equal("value", stringValue);
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
        int algorithm = DecodeInt(headers.Headers[1]);
        Assert.Equal(-7, algorithm);

        // Check key ID
        string keyId = DecodeString(headers.Headers[4]);
        Assert.Equal("my-key-id", keyId);

        // Check custom issuer
        string issuer = DecodeString(headers.Headers["iss"]);
        Assert.Equal("https://issuer", issuer);

        // Check hashed flag
        bool hashed = DecodeBool(headers.Headers["hashed"]);
        Assert.True(hashed);
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

        // Check that we can find the keys by iterating
        bool foundIntKey = false;
        bool foundStringKey = false;

        foreach ((CborLabel key, CborEncodedValue value) in deserialized.Headers)
        {
            switch (key.Value)
            {
                case long l when l == 1:
                case int i when i == 1:
                    foundIntKey = true;
                    int algorithm = DecodeInt(value);
                    Assert.Equal(-7, algorithm);
                    break;
                case string s when s == "custom":
                    foundStringKey = true;
                    string custom = DecodeString(value);
                    Assert.Equal("test", custom);
                    break;
                default:
                    break;
            }
        }

        Assert.True(foundIntKey, "Should find integer key");
        Assert.True(foundStringKey, "Should find string key");
    }

    private static bool DecodeBool(CborEncodedValue encoded)
    {
        CborReader reader = new(encoded.Value.Span);
        return reader.ReadBoolean();
    }

    private static int DecodeInt(CborEncodedValue encoded)
    {
        CborReader reader = new(encoded.Value.Span);
        return reader.ReadInt32();
    }

    private static string DecodeString(CborEncodedValue encoded)
    {
        CborReader reader = new(encoded.Value.Span);
        return reader.ReadString()!;
    }
}
