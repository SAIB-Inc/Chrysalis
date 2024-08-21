using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using Xunit;

namespace Chrysalis.Cbor.Tests
{
    public class CborSerializerTests
    {
        [Fact]
        public void SerializeAndDeserializePrimitives()
        {
            // Arrange
            var testData = new
            {
                ByteArray = new byte[] { 0x01, 0x02, 0x03 },
                ByteArrayAsHex = "010203",
                Int = 42,
                Long = (long)int.MaxValue + 1,
                ULong = (ulong)long.MaxValue + 1
            };

            // Act
            var serializedByteArray = CborSerializer.Serialize(testData.ByteArray);
            var serializedByteArrayAsHex = CborSerializer.Serialize(testData.ByteArrayAsHex);
            var serializedInt = CborSerializer.Serialize(testData.Int);
            var serializedLong = CborSerializer.Serialize(testData.Long);
            var serializedULong = CborSerializer.Serialize(testData.ULong);

            var deserializedByteArray = (byte[])CborSerializer.Deserialize(serializedByteArray, typeof(byte[]));
            var deserializedByteArrayAsHex = (string)CborSerializer.Deserialize(serializedByteArrayAsHex, typeof(string));
            var deserializedInt = (int)CborSerializer.Deserialize(serializedInt, typeof(int));
            var deserializedLong = (long)CborSerializer.Deserialize(serializedLong, typeof(long));
            var deserializedULong = (ulong)CborSerializer.Deserialize(serializedULong, typeof(ulong));

            // Assert
            Assert.Equal(testData.ByteArray, deserializedByteArray);
            Assert.Equal(testData.ByteArrayAsHex, deserializedByteArrayAsHex);
            Assert.Equal(testData.Int, deserializedInt);
            Assert.Equal(testData.Long, deserializedLong);
            Assert.Equal(testData.ULong, deserializedULong);
        }

    //     [Fact]
    //     public void SerializeAndDeserializeArray()
    //     {
    //         // Arrange
    //         int[] testArray = [1, 2, 3, 4, 5];

    //         // Act
    //         var serialized = CborSerializer.SerializeList(testArray);
    //         var deserialized = (int[])CborSerializer.DeserializeList(serialized, typeof(int[]));

    //         // Assert
    //         Assert.Equal(testArray, deserialized);
    //     }

    //     [Fact]
    //     public void SerializeAndDeserializeMap()
    //     {
    //         // Arrange
    //         var testMap = new Dictionary<string, int>
    //         {
    //             { "one", 1 },
    //             { "two", 2 },
    //             { "three", 3 }
    //         };

    //         using (var ms = new MemoryStream(serialized))
    //         using (CborReader reader = new(ms))

    //         // Act
    //         var serialized = CborSerializer.DeserializeMap(CborReader reader, testMap);
    //         var deserialized = (Dictionary<string, int>)CborSerializer.Deserialize(serialized, typeof(Dictionary<string, int>));

    //         // Assert
    //         Assert.Equal(testMap, deserialized);
    //     }

    //     [Fact]
    //     public void SerializeAndDeserializeUnion()
    //     {
    //         // Arrange
    //         var unionTypes = new[] { typeof(int), typeof(string), typeof(byte[]) };
    //         var testUnion = (object)42;

    //         // Act
    //         var serialized = CborSerializer.SerializeUnion(testUnion, unionTypes);
    //         var deserialized = CborSerializer.DeserializeUnion(serialized, unionTypes, typeof(object));

    //         // Assert
    //         Assert.Equal(testUnion, deserialized);
    //     }
    }
}