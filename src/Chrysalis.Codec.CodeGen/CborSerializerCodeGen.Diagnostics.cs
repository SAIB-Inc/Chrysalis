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
}
