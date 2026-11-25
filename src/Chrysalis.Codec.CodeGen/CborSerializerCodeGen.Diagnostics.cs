using Microsoft.CodeAnalysis;

namespace Chrysalis.Codec.CodeGen;

public sealed partial class CborSerializerCodeGen
{
    private static readonly DiagnosticDescriptor UnknownUnionShape = new(
        "CHCB001", "Union case cannot be inferred", "Union '{0}' has no context-free discriminator for these cases",
        "Chrysalis.Codec", DiagnosticSeverity.Info, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor OverlappingUnionShape = new(
        "CHCB002", "Union case shapes overlap", "Union '{0}' has cases with identical wire shapes; ambiguous bytes will be rejected",
        "Chrysalis.Codec", DiagnosticSeverity.Info, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidDeclaredUnionReader = new(
        "CHCB003", "Invalid declared union reader",
        "Union '{0}' must declare public static Read(ReadOnlyMemory<byte>, out int) returning a non-null union value, and leave Read(ReadOnlyMemory<byte>) to the generator",
        "Chrysalis.Codec", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor UnchunkedPlutusBytes = new(
        "CHCB004", "Plutus data bytes written in one piece",
        "Field '{0}' has byte strings inside a generic type, which are written in one piece and refused by the ledger over 64 bytes; use PlutusBoundedBytes there",
        "Chrysalis.Codec", DiagnosticSeverity.Warning, isEnabledByDefault: true);
}
