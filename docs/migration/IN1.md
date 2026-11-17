# Dependencies

- Upgrade direct `SAIB.Cbor` references from 2.0.0 to 3.1.0 when upgrading Chrysalis. Its types remain part of Chrysalis's public API, including `CborAny.FromPrimitive`'s writer parameter; keeping a direct 2.x reference can cause a package downgrade error.
- Upgrade `SAIB.Blake2Fast` from 3.0.1 to 4.0.0. The 4.0.0 package targets .NET 11.
- `Chrysalis.Wallet` no longer brings `Microsoft.AspNetCore.Cryptography.KeyDerivation`. Replace uses of `KeyDerivation.Pbkdf2` with the base-library `Rfc2898DeriveBytes` APIs, or add an explicit package reference if the application still needs that package.
- `Chrysalis.Wallet` no longer brings `SauceControl.Blake2Fast`. Use `SAIB.Blake2Fast` 4.0.0 for the `Blake2Fast` API instead of relying on that removed transitive package.
