using System;
using System.Collections.Generic;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types;
using Xunit;

namespace Chrysalis.Cbor.Test;

public class CborPrimitiveTests
{
    [Fact]
    public void CborPrimitive_ImplicitConversions_Work()
    {
        // Arrange & Act
        CborPrimitive intPrimitive = 42;
        CborPrimitive longPrimitive = 42L;
        CborPrimitive stringPrimitive = "test";
        CborPrimitive boolPrimitive = true;
        CborPrimitive bytesPrimitive = new byte[] { 1, 2, 3 };
        
        // Assert
        Assert.IsType<CborInt>(intPrimitive);
        Assert.IsType<CborLong>(longPrimitive);
        Assert.IsType<CborString>(stringPrimitive);
        Assert.IsType<CborBool>(boolPrimitive);
        Assert.IsType<CborBytes>(bytesPrimitive);
    }

    [Fact]
    public void CborPrimitive_Int_RoundTrip()
    {
        // Arrange
        CborPrimitive primitive = 42;
        
        // Act
        byte[] bytes = CborSerializer.Serialize(primitive);
        CborPrimitive deserialized = CborSerializer.Deserialize<CborPrimitive>(bytes);
        
        // Assert
        Assert.IsType<CborInt>(deserialized);
        CborInt result = (CborInt)deserialized;
        Assert.Equal(42, result.Value);
    }
    
    [Fact]
    public void CborPrimitive_String_RoundTrip()
    {
        // Arrange
        CborPrimitive primitive = "hello";
        
        // Act
        byte[] bytes = CborSerializer.Serialize(primitive);
        CborPrimitive deserialized = CborSerializer.Deserialize<CborPrimitive>(bytes);
        
        // Assert
        Assert.IsType<CborString>(deserialized);
        CborString result = (CborString)deserialized;
        Assert.Equal("hello", result.Value);
    }
    
    [Fact]
    public void CborPrimitive_Bool_RoundTrip()
    {
        // Arrange
        CborPrimitive primitive = true;
        
        // Act
        byte[] bytes = CborSerializer.Serialize(primitive);
        CborPrimitive deserialized = CborSerializer.Deserialize<CborPrimitive>(bytes);
        
        // Assert
        Assert.IsType<CborBool>(deserialized);
        CborBool result = (CborBool)deserialized;
        Assert.True(result.Value);
    }
}