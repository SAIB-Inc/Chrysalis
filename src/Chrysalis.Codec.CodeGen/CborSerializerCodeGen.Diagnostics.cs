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

    private static readonly DiagnosticDescriptor InvalidOfficialUnionDeclaration = new(
        "CHCB005", "Invalid official union declaration",
        "Official union '{0}' must be a public top-level non-ref readonly partial struct annotated with [Union] and implementing ICborType, without [CborMap], [CborList], [CborConstr], or [PlutusData]; legacy [CborUnion] interfaces and classes cannot specify case types",
        "Chrysalis.Codec", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidOfficialUnionCaseList = new(
        "CHCB006", "Invalid official union case list",
        "Official union '{0}' has an invalid case list: {1}",
        "Chrysalis.Codec", DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidOfficialUnionCaseType = new(
        "CHCB007", "Invalid official union case type",
        "Case type '{1}' of official union '{0}' must be a concrete non-union type implementing ICborType with provable wire shape and either annotated with [CborSerializable]/[PlutusData] or declaring public static Read and Write methods",
        "Chrysalis.Codec", DiagnosticSeverity.Error, isEnabledByDefault: true);
}
