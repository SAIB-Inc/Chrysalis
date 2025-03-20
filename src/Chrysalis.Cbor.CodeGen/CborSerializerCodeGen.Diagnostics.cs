using Microsoft.CodeAnalysis;

namespace Chrysalis.Cbor.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private static class CborDiagnostics
    {
        public static readonly DiagnosticDescriptor CborAttributeNotFound = new(
            id: "CBOR001",
            title: "CborSerializable attribute not found",
            messageFormat: "The type '{0}' does not have the CborSerializable attribute",
            category: "Cbor",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MultipleCborAttributes = new(
            id: "CBOR002",
            title: "Multiple CborSerializable attributes found",
            messageFormat: "The type '{0}' has multiple CborSerializable attributes",
            category: "Cbor",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);
    }
}