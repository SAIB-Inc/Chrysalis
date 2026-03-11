namespace Chrysalis.Plutus.Types;

/// <summary>
/// A UPLC program: a version triple plus a term body.
/// Version is typically 1.1.0 for modern Plutus scripts.
/// </summary>
public sealed record Program<TBinder>(Version Version, Term<TBinder> Term);

/// <summary>
/// Plutus Core version number (major.minor.patch).
/// </summary>
public readonly record struct Version(int Major, int Minor, int Patch)
{
    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}
