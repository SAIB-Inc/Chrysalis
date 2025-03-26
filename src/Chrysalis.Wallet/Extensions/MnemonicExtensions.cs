using Chrysalis.Wallet.Keys;
using Chrysalis.Wallet.Models.Enums;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace Chrysalis.Wallet.Extensions;

public static class MnemonicExtensions
{
    //  *  1. rootKey[0] &= 248 | 0b1111_1000; clearing the 3 lower bits.
    //  *  2. rootKey[31] &= 31 | 0b0001_1111; clearing the three highest bits.
    //  *  3. rootKey[31] |= 64 | 0b0100_0000; setting the second-highest bit.
    public static PrivateKey GetRootKey(this Mnemonic mnemonic, string password = "")
    {
        byte[] rootKey = KeyDerivation.Pbkdf2(password, mnemonic.Entropy, KeyDerivationPrf.HMACSHA512, 4096, 96);
        rootKey[0] &= 0b1111_1000;
        rootKey[31] &= 0b0001_1111;
        rootKey[31] |= 0b0100_0000;

        return new PrivateKey(rootKey.Slice(0, 64), rootKey.Slice(64));
    }

    public static PrivateKey GetAccountKey(this Mnemonic mnemonic, int accountIndex = 0)
        => mnemonic
            .GetRootKey()
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(accountIndex, DerivationType.HARD);
}