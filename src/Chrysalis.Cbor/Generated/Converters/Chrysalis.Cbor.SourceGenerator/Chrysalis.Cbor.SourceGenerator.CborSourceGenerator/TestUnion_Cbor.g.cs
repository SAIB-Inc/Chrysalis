﻿// <auto-generated/>
using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using Chrysalis.Cbor.Types;

namespace Chrysalis.Cbor.Types.Test;

public partial record TestUnion
{
    // Serialization implementation
    public static new void Write(CborWriter writer, global::Chrysalis.Cbor.Types.Test.TestUnion value)
    {
        // Determine the concrete type from its type name
        switch (value.CborTypeName)
        {
            case "TestListUnion":
                global::Chrysalis.Cbor.Types.Test.TestUnion.TestListUnion.Write(writer, (global::Chrysalis.Cbor.Types.Test.TestUnion.TestListUnion)value);
                break;
            case "TestConstrUnion":
                global::Chrysalis.Cbor.Types.Test.TestUnion.TestConstrUnion.Write(writer, (global::Chrysalis.Cbor.Types.Test.TestUnion.TestConstrUnion)value);
                break;
            case "TestMapUnion":
                global::Chrysalis.Cbor.Types.Test.TestUnion.TestMapUnion.Write(writer, (global::Chrysalis.Cbor.Types.Test.TestUnion.TestMapUnion)value);
                break;
            case "NullableTestMapUnion":
                global::Chrysalis.Cbor.Types.Test.TestUnion.NullableTestMapUnion.Write(writer, (global::Chrysalis.Cbor.Types.Test.TestUnion.NullableTestMapUnion)value);
                break;
            default:
                throw new Exception($"Unknown union type: {value.CborTypeName}");
        }

    }

    // Deserialization implementation
    public static new global::Chrysalis.Cbor.Types.Test.TestUnion Read(ReadOnlyMemory<byte> data)
    {
        // Try each union case
        var originalData = data;
        Exception lastException = null;
        // Try TestListUnion
        try
        {
            var result = global::Chrysalis.Cbor.Types.Test.TestUnion.TestListUnion.Read(originalData);
            return result;
        }
        catch (Exception ex)
        {
            lastException = ex;
        }
        // Try TestConstrUnion
        try
        {
            var result = global::Chrysalis.Cbor.Types.Test.TestUnion.TestConstrUnion.Read(originalData);
            return result;
        }
        catch (Exception ex)
        {
            lastException = ex;
        }
        // Try TestMapUnion
        try
        {
            var result = global::Chrysalis.Cbor.Types.Test.TestUnion.TestMapUnion.Read(originalData);
            return result;
        }
        catch (Exception ex)
        {
            lastException = ex;
        }
        // Try NullableTestMapUnion
        try
        {
            var result = global::Chrysalis.Cbor.Types.Test.TestUnion.NullableTestMapUnion.Read(originalData);
            return result;
        }
        catch (Exception ex)
        {
            lastException = ex;
        }
        throw new Exception("Could not deserialize union type", lastException);

    }
}