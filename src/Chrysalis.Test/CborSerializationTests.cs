using System.Formats.Cbor;
using System.Reflection;
using Chrysalis.Cardano.Models;
using Chrysalis.Cbor;
using Xunit;

namespace Chrysalis.Test
{
    public class CborSerializerTests
    {
        [Theory]
        [InlineData("1834", typeof(CborInt))] // Example hex for CBOR int 52
        [InlineData("4101", typeof(CborBytes))] // Example hex for CBOR bytes {0x01)]
        [InlineData("1a000f4240", typeof(CborUlong))] // Example hex for CBOR ulong 1_000_000
        [InlineData("43414243", typeof(CborBytes))] // Example hex for CBOR bytes of `ABC` string
        [InlineData("a141614541696b656e", typeof(CborMap<CborBytes, CborBytes>))]
        [InlineData("9f0102030405ff", typeof(CborList<CborInt>))] // [_ 1, 2, 3, 4, 5]
        [InlineData("d8799f182aff", typeof(Option<CborInt>))] // Serialized CBOR for Option::Some(42):
        [InlineData("d87a80", typeof(Option<CborInt>))] // Serialized CBOR for Option::None:
        public void SerializeAndDeserializePrimitives(string cborHex, Type type)
        {
            // Arrange
            byte[] cborBytes = Convert.FromHexString(cborHex);

            // Act
            MethodInfo? deserializeMethod = typeof(CborSerializer).GetMethod(nameof(CborSerializer.Deserialize));
            Assert.NotNull(deserializeMethod);

            MethodInfo? genericDeserializeMethod = deserializeMethod?.MakeGenericMethod(type);
            Assert.NotNull(genericDeserializeMethod);

            object? cborObject = genericDeserializeMethod?.Invoke(null, [cborBytes, null]);
            Assert.NotNull(cborObject);
            
            var serializedBytes = CborSerializer.Serialize((ICbor)cborObject);
            string serializedHex = Convert.ToHexString(serializedBytes).ToLowerInvariant();

            // Assert
            Assert.Equal(cborHex, serializedHex);
        }
    }
}