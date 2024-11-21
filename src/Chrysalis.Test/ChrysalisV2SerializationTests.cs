using System.Collections;
using System.Text;
using ChrysalisV2.Extensions.Core;
using ChrysalisV2.Types.Core;
using ChrysalisV2.Types.Custom.Test;
using Xunit;

namespace Chrysalis.Test;

public class CborSerializerV2Tests
{

    private static void SerializeBaseTest(string expectedValue, object? value, Type type)
    {
        // Arrange: Create an instance of the given type
        object? instance = value is null ? Activator.CreateInstance(type) : Activator.CreateInstance(type, (object[])value);

        if (instance is not ICbor cborInstance)
        {
            throw new InvalidOperationException($"Type {type.Name} does not implement ICbor.");
        }

        // Act: Serialize the instance
        byte[] serializedBytes = cborInstance.Serialize();

        // Assert: Check that serialization returns a non-null, non-empty byte array
        Assert.NotNull(serializedBytes);
        Assert.NotEmpty(serializedBytes);

        string actualValue = Convert.ToHexString(serializedBytes).ToLowerInvariant();
        Assert.Equal(actualValue, expectedValue);
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
    [MemberData(nameof(ChrysalisV2TestData.GetCborConstrTestData), MemberType = typeof(ChrysalisV2TestData))]
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

}

public static class ChrysalisV2TestData
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