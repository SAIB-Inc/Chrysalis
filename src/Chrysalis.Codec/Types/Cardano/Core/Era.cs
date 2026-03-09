namespace Chrysalis.Codec.Types.Cardano.Core;

/// <summary>
/// Cardano blockchain eras.
/// </summary>
public enum Era
{
    /// <summary>Byron era (including epoch boundary blocks).</summary>
    Byron = 0,

    /// <summary>Shelley era.</summary>
    Shelley = 1,

    /// <summary>Allegra era.</summary>
    Allegra = 2,

    /// <summary>Mary era.</summary>
    Mary = 3,

    /// <summary>Alonzo era.</summary>
    Alonzo = 4,

    /// <summary>Babbage era.</summary>
    Babbage = 5,

    /// <summary>Conway era.</summary>
    Conway = 6
}
