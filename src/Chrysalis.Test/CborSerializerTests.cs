using System.Collections;
using System.Reflection;
using System.Text;
using Chrysalis.Attributes;
using Chrysalis.Converters;
using Chrysalis.Types.Core;
using Chrysalis.Types.Custom.Test;
using Xunit;

namespace Chrysalis.Test;

public class CborSerializerTests
{

    private static void SerializeBaseTest(string expectedValue, object? value, Type type)
    {
        // Step 1: Create an instance of the type being tested
        object? instance = (value is null
            ? Activator.CreateInstance(type)
            : Activator.CreateInstance(type, (object[])value))
            ?? throw new InvalidOperationException($"Could not create an instance of type {type.Name}.");

        // Step 2: Retrieve the CborSerializable attribute
        var attribute = (CborSerializableAttribute?)Attribute.GetCustomAttribute(type, typeof(CborSerializableAttribute));
        if (attribute == null || attribute.Converter == null)
        {
            throw new InvalidOperationException($"Type {type.Name} does not have a valid CborSerializable attribute with a Converter.");
        }

        // Step 3: Get the converter type and validate it
        var converterType = attribute.Converter;
        var genericInterface = converterType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICborConverter<>))
            ?? throw new InvalidOperationException($"Converter type {converterType.Name} does not implement ICborConverter<T>.");

        // Step 4: Create an instance of the converter
        var converter = Activator.CreateInstance(converterType)
            ?? throw new InvalidOperationException($"Could not create an instance of the converter {converterType.Name}.");

        // Step 5: Find the correct Serialize method on the converter
        var serializeMethod = genericInterface.GetMethod("Serialize")
            ?? throw new InvalidOperationException($"Serialize method not found on converter {converterType.Name}.");

        // Step 6: Invoke the Serialize method with the instance (of the correct type)
        var result = serializeMethod.Invoke(converter, [instance]);
        if (result is not ReadOnlyMemory<byte> memoryData)
        {
            throw new InvalidOperationException($"Serialize method did not return a ReadOnlyMemory<byte>.");
        }

        // Convert ReadOnlyMemory<byte> to byte[]
        byte[] serializedBytes = memoryData.ToArray();

        // Step 7: Assert that the result matches the expected value
        Assert.NotNull(serializedBytes);
        Assert.NotEmpty(serializedBytes);

        string actualValue = Convert.ToHexString(serializedBytes).ToLowerInvariant();
        Assert.Equal(expectedValue, actualValue);
    }


    [Theory]
    [InlineData("f5", true, typeof(CborBool))]
    [InlineData("f4", false, typeof(CborBool))]
    public void SerializeBool(string expectedValue, object value, Type type)
    {
        value = new object[] { (bool)value };
        SerializeBaseTest(expectedValue, value, type);
    }

    [Theory]
    [MemberData(nameof(ChrysalisTestData.GetCborConstrTestData), MemberType = typeof(ChrysalisTestData))]
    public void SerializeCborConstr(string expectedValue, object[]? value, Type type)
    {
        SerializeBaseTest(expectedValue, value, type);
    }

    [Theory]
    [InlineData("4568656c6c6f", "hello", typeof(CborBytes))]
    [InlineData("5f5840746869736973616c6f6e67737472696e6774657374746f74657374626f756e646564627974657372616e646f6d72616e646f6d72616e646f6d72616e646f6d72584072616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e6f58406d6472616f6e646e6473616e6f696e646f736e616f646e61736f646e6f61736e646f61736f646e61736f646e6f6173646e616f736e646f61736f646e61736f64506e61736f646e6f61736e646f61736f6eff",
        "thisisalongstringtesttotestboundedbytesrandomrandomrandomrandomrrandomrandomrandomrandomrandomrandomrandomrandomrandomrandomranomdraondndsanoindosnaodnasodnoasndoasodnasodnoasdnaosndoasodnasodnasodnoasndoason",
        typeof(CborBoundedBytesTest)
    )]
    public void SerializeCborBytes(string expectedValue, object value, Type type)
    {
        value = new object[] { Encoding.UTF8.GetBytes((string)value) };
        SerializeBaseTest(expectedValue, value, type);
    }

    [Theory]
    [InlineData(true, "f5", typeof(UnionBool))]  // true -> CBOR true
    [InlineData(false, "f4", typeof(UnionBool))] // false -> CBOR false
    [InlineData("hello", "4568656c6c6f", typeof(UnionBytes))] // "hello" -> CBOR bytes
    [InlineData("thisisalongstringtesttotestboundedbytesrandomrandomrandomrandomr",
    "5840746869736973616c6f6e67737472696e6774657374746f74657374626f756e646564627974657372616e646f6d72616e646f6d72616e646f6d72616e646f6d72",
    typeof(UnionBoundedBytes))]
    public void SerializeUnion(object value, string expectedHex, Type unionType)
    {
        // Arrange
        ICborUnionBasic union = unionType switch
        {
            var t when t == typeof(UnionBool) => new UnionBool((bool)value),
            var t when t == typeof(UnionBytes) || t == typeof(UnionBoundedBytes) =>
                (ICborUnionBasic)Activator.CreateInstance(unionType, [Encoding.UTF8.GetBytes((string)value)])!,
            _ => throw new ArgumentException($"Unexpected union type: {unionType}")
        };

        // Get converter through reflection
        Type interfaceType = typeof(ICborUnionBasic);
        CborSerializableAttribute? attribute = (CborSerializableAttribute?)Attribute.GetCustomAttribute(interfaceType, typeof(CborSerializableAttribute));
        if (attribute?.Converter == null)
        {
            throw new InvalidOperationException($"Type {interfaceType.Name} does not have a valid CborSerializable attribute with a Converter.");
        }

        Type converterType = attribute.Converter;
        Type genericInterface = converterType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICborConverter<>))
            ?? throw new InvalidOperationException($"Converter type {converterType.Name} does not implement ICborConverter<T>.");

        object converter = Activator.CreateInstance(converterType)
            ?? throw new InvalidOperationException($"Could not create an instance of the converter {converterType.Name}.");

        MethodInfo serializeMethod = genericInterface.GetMethod("Serialize")
            ?? throw new InvalidOperationException($"Serialize method not found on converter {converterType.Name}.");

        // Act 
        byte[] result = ((ReadOnlyMemory<byte>)serializeMethod.Invoke(converter, [union])!).ToArray();

        // Assert
        Assert.Equal(expectedHex, Convert.ToHexString(result).ToLower());
    }
}

public static class ChrysalisTestData
{
    // Create a static method that returns a list of test data
    public static IEnumerable<object[]> GetCborConstrTestData()
    {
        // Creating test cases for CborConstr with different items
        yield return new object[] { "d8799fff", null, typeof(CborConstr) };
        yield return new object[] { "d87a9fff", null, typeof(CborConstrTestIndex1) };
        yield return new object[] { "d8799f4b68656c6c6f5f776f726c645f5840746869736973616c6f6e67737472696e6774657374746f74657374626f756e646564627974657372616e646f6d72616e646f6d72616e646f6d72616e646f6d72584072616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e6f58406d6472616f6e646e6473616e6f696e646f736e616f646e61736f646e6f61736e646f61736f646e61736f646e6f6173646e616f736e646f61736f646e61736f64506e61736f646e6f61736e646f61736f6effff",
            new object[]{
                new CborBytes(Encoding.UTF8.GetBytes("hello_world")),
                new CborBoundedBytesTest(Encoding.UTF8.GetBytes("thisisalongstringtesttotestboundedbytesrandomrandomrandomrandomrrandomrandomrandomrandomrandomrandomrandomrandomrandomrandomranomdraondndsanoindosnaodnasodnoasndoasodnasodnoasdnaosndoasodnasodnasodnoasndoason")),
            },
            typeof(CborConstrTestWithParams)
        };
        yield return new object[] { "d8799fd8799f4b68656c6c6f5f776f726c645f5840746869736973616c6f6e67737472696e6774657374746f74657374626f756e646564627974657372616e646f6d72616e646f6d72616e646f6d72616e646f6d72584072616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e646f6d72616e6f58406d6472616f6e646e6473616e6f696e646f736e616f646e61736f646e6f61736e646f61736f646e61736f646e6f6173646e616f736e646f61736f646e61736f64506e61736f646e6f61736e646f61736f6effffd87a80ff",
            new object[]{
                new CborConstrTestWithParams(
                    new CborBytes(Encoding.UTF8.GetBytes("hello_world")),
                    new CborBoundedBytesTest(Encoding.UTF8.GetBytes("thisisalongstringtesttotestboundedbytesrandomrandomrandomrandomrrandomrandomrandomrandomrandomrandomrandomrandomrandomrandomranomdraondndsanoindosnaodnasodnoasndoasodnasodnoasdnaosndoasodnasodnasodnoasndoason"))
                ),
                new CborConstrTestDefinite()
            },
            typeof(CborConstrTestNested)
        };
    }
}