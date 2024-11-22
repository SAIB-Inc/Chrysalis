using System.Reflection;
using System.Text;
using Chrysalis.Attributes;
using Chrysalis.Converters;
using Chrysalis.Types.Core;
using Chrysalis.Types.Custom.Test;
using Xunit;

namespace Chrysalis.Test;

public class CborDeserializerTests
{
    private static void DeserializeBaseTest(string hexValue, object? expectedValue, Type type)
    {
        // Step 1: Convert hex string to byte array
        byte[] inputBytes = Convert.FromHexString(hexValue);

        // Step 2: Retrieve the CborSerializable attribute
        CborSerializableAttribute? attribute = (CborSerializableAttribute?)Attribute.GetCustomAttribute(type, typeof(CborSerializableAttribute));
        if (attribute == null || attribute.Converter == null)
        {
            throw new InvalidOperationException($"Type {type.Name} does not have a valid CborSerializable attribute with a Converter.");
        }

        // Step 3: Get the converter type and validate it
        Type converterType = attribute.Converter;
        Type genericInterface = converterType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICborConverter<>))
            ?? throw new InvalidOperationException($"Converter type {converterType.Name} does not implement ICborConverter<T>.");

        // Step 4: Create an instance of the converter
        object converter = Activator.CreateInstance(converterType)
            ?? throw new InvalidOperationException($"Could not create an instance of the converter {converterType.Name}.");

        // Step 5: Find the correct Deserialize method on the converter
        MethodInfo deserializeMethod = genericInterface.GetMethod("Deserialize")
            ?? throw new InvalidOperationException($"Deserialize method not found on converter {converterType.Name}.");

        // Step 6: Invoke the Deserialize method with the byte data
        object? result = deserializeMethod.Invoke(converter, [new ReadOnlyMemory<byte>(inputBytes)]);

        // Step 7: Create expected instance if needed
        object? expected;
        if (expectedValue is null)
        {
            expected = Activator.CreateInstance(type);
        }
        else if (expectedValue is object[] arr)
        {
            expected = Activator.CreateInstance(type, arr);
        }
        else
        {
            expected = Activator.CreateInstance(type, [expectedValue]);
        }

        if (expected == null)
        {
            throw new InvalidOperationException($"Could not create an instance of type {type.Name}.");
        }

        // Step 8: Assert that the result matches the expected value
        Assert.NotNull(result);
        if (result is byte[] resultBytes && expected is byte[] expectedBytes)
        {
            // Use SequenceEqual for byte array comparison
            Assert.True(resultBytes.SequenceEqual(expectedBytes),
                $"Byte arrays do not match.\nExpected: {Convert.ToHexString(expectedBytes)}\nActual: {Convert.ToHexString(resultBytes)}");
        }
        else
        {
            Assert.Equal(expected, result);
        }
    }


    [Theory]
    [InlineData("f5", true, typeof(CborBool))]
    [InlineData("f4", false, typeof(CborBool))]
    public void DeserializeBool(string hexValue, object expectedValue, Type type)
    {
        DeserializeBaseTest(hexValue, expectedValue, type);
    }

    [Theory]
    [InlineData("4568656c6c6f", "hello", typeof(CborBytes))]
    [InlineData("5f5840746869736973616c6f6e67737472696e6774657374746f74657374626f756e646564627974657372616e646f6d72616e646f6d72616e646f6d72616e646f6d72584072616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e6f58406d6472616f6e646e6473616e6f696e646f736e616f646e61736f646e6f61736e646f61736f646e61736f646e6f6173646e616f736e646f61736f646e61736f64506e61736f646e6f61736e646f61736f6eff",
        "thisisalongstringtesttotestboundedbytesrandomrandomrandomrandomrrandomrandomrandomrandomrandomrandomrandomrandomrandomrandomranomdraondndsanoindosnaodnasodnoasndoasodnasodnoasdnaosndoasodnasodnasodnoasndoason",
        typeof(CborBoundedBytesTest)
    )]
    public void DeserializeCborBytes(string hexValue, string value, Type type)
    {
        // Convert string value to expected bytes
        byte[] expectedBytes = Encoding.UTF8.GetBytes(value);
        byte[] inputBytes = Convert.FromHexString(hexValue);

        // Get converter through reflection
        CborSerializableAttribute? attribute = (CborSerializableAttribute?)Attribute.GetCustomAttribute(type, typeof(CborSerializableAttribute));
        if (attribute?.Converter == null)
        {
            throw new InvalidOperationException($"Type {type.Name} does not have a valid CborSerializable attribute with a Converter.");
        }

        Type converterType = attribute.Converter;
        Type genericInterface = converterType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICborConverter<>))
            ?? throw new InvalidOperationException($"Converter type {converterType.Name} does not implement ICborConverter<T>.");

        object converter = Activator.CreateInstance(converterType)
            ?? throw new InvalidOperationException($"Could not create an instance of the converter {converterType.Name}.");

        MethodInfo deserializeMethod = genericInterface.GetMethod("Deserialize")
            ?? throw new InvalidOperationException($"Deserialize method not found on converter {converterType.Name}.");

        // Act
        object? result = deserializeMethod.Invoke(converter, [new ReadOnlyMemory<byte>(inputBytes)]);

        // Get the Value property which should contain the byte array
        PropertyInfo? valueProperty = result?.GetType().GetProperty("Value");
        byte[]? resultBytes = valueProperty?.GetValue(result) as byte[];

        // Assert
        Assert.NotNull(resultBytes);
        Assert.True(expectedBytes.SequenceEqual(resultBytes),
            $"Expected: {Convert.ToHexString(expectedBytes)}, Got: {Convert.ToHexString(resultBytes)}");

        ICborUnionBasic cborUnionBasic = CborConverter.Deserialize<ICborUnionBasic>(resultBytes);
    }
}