using Chrysalis.Plutus.Types;

namespace Chrysalis.Plutus.Text;

/// <summary>
/// Public API for parsing UPLC text format.
/// </summary>
public static class UplcParser
{
    /// <summary>
    /// Parses a UPLC text program into a named AST.
    /// </summary>
    public static Program<Name> Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Parser parser = new(source);
        return parser.ParseProgram();
    }

    /// <summary>
    /// Parses a UPLC text program and converts to DeBruijn-indexed AST.
    /// </summary>
    public static Program<DeBruijn> ParseToDeBruijn(string source)
    {
        Program<Name> named = Parse(source);
        return NameToDeBruijn(named);
    }

    /// <summary>
    /// Converts a named program to DeBruijn-indexed form.
    /// </summary>
    public static Program<DeBruijn> NameToDeBruijn(Program<Name> program)
    {
        ArgumentNullException.ThrowIfNull(program);
        DeBruijnConverter converter = new();
        return converter.Convert(program);
    }

    /// <summary>
    /// Pretty-prints a DeBruijn-indexed term to UPLC text format.
    /// </summary>
    public static string PrettyPrint(Term<DeBruijn> term)
    {
        ArgumentNullException.ThrowIfNull(term);
        return PrettyPrinter.Print(term);
    }

    /// <summary>
    /// Pretty-prints a DeBruijn-indexed program to UPLC text format.
    /// </summary>
    public static string PrettyPrint(Program<DeBruijn> program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return PrettyPrinter.Print(program);
    }
}
