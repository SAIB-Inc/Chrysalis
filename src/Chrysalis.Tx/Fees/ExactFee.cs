using Chrysalis.Codec.Types;

namespace Chrysalis.Tx.Fees;

internal static class ExactFee
{
    internal const ulong MaxReferenceScriptSize = 204_800;

    // Divide before multiplying the wide operand. The remainder times the numerator fits in 128 bits.
    private static (UInt128 Whole, UInt128 Remainder) MultiplyDivide(UInt128 units, CborRationalNumber price)
    {
        ulong denominator = Denominator(price);
        UInt128 tail = units % denominator * price.Numerator;
        return (checked((units / denominator * price.Numerator) + (tail / denominator)), tail % denominator);
    }

    internal static UInt128 Script(UInt128 mem, UInt128 steps, CborRationalNumber memPrice, CborRationalNumber stepPrice)
    {
        (UInt128 memWhole, UInt128 memRemainder) = MultiplyDivide(mem, memPrice);
        (UInt128 stepWhole, UInt128 stepRemainder) = MultiplyDivide(steps, stepPrice);
        // Compare one remainder to the complement of the other; adding their cross-products can overflow.
        uint carry = (memRemainder | stepRemainder) == 0 ? 0U
            : memRemainder * stepPrice.Denominator <= (memPrice.Denominator * (stepPrice.Denominator - stepRemainder)) ? 1U : 2U;
        return checked(memWhole + stepWhole + carry);
    }

    internal static UInt128 Reference(ulong size, CborRationalNumber price)
    {
        const ulong tierSize = 25_600;
        // Eight bounded tiers share denominator price.Denominator * 5^7. Floor just once.
        const ulong fifths = 78_125;
        UInt128 denominator = (UInt128)Denominator(price) * fifths;
        ulong weight = fifths;
        UInt128 numerator = 0;
        while (size > 0)
        {
            ulong bytes = Math.Min(size, tierSize);
            numerator += (UInt128)bytes * weight * price.Numerator;
            size -= bytes;
            weight = weight / 5 * 6;
        }
        return numerator / denominator;
    }

    internal static UInt128 Collateral(ulong fee, ulong percentage)
    {
        UInt128 product = (UInt128)fee * percentage;
        return (product / 100) + (product % 100 == 0 ? 0U : 1U);
    }

    internal static UInt128 Linear(ulong size, ulong coefficient, UInt128 constant) => checked(((UInt128)size * coefficient) + constant);

    private static ulong Denominator(CborRationalNumber price) => price.Denominator != 0 ? price.Denominator
        : throw new InvalidDataException("A fee price has a zero denominator.");
}
