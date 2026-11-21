# serialiseData and applied scripts write the node's bytes; deep data and scripts no longer end the process

`CborWriter.EncodePlutusData`, which the `serialiseData` builtin uses, now writes Plutus data exactly as the node does: a non-empty list, or the fields of a constructor, as an indefinite-length array, and an empty one as `80`, whatever form the data was read in and whatever `IsDefinite` says. Before, data read with definite-length lists was written back that way, and an empty list built in code was written `9fff`, so a script that hashed `serialiseData` of such data got another hash than on chain. To keep the bytes a datum came with, keep those bytes rather than encoding the data again.

`FlatEncoder` now writes data constants the same way, as plutus does, so `ScriptApplicator.ApplyParameter`, `ScriptApplicator.ApplyParameters` and `ScriptExtension.ApplyParameters` write the data in an applied script as plutus's tools write it. Before, constructor fields were always written definite, in the parameter and in the script's own constants. So a script applied with an earlier version can now get other bytes, and with them another hash and address, when the script or a parameter holds a constructor with fields, or a list not already in the node's form: a non-empty list written definite, or an empty one written `9fff`. To spend from an address made that way, a transaction must carry that exact earlier script, in its witnesses or as a reference script whose hash matches: save the applied script bytes from the earlier version before you upgrade, or check that such a reference script is on chain. The address or hash alone is only a check, not a way to spend. `CborWriter.EncodePlutusDataDefinite` writes the same bytes as before.

Reading Plutus data, decoding scripts, `equalsData`, `serialiseData` and the scope check of `Evaluator.EvaluateScript` no longer recurse once per nesting level, so deep data and terms no longer overflow the stack and end the process there. For example, data nested 16,385 levels, which the node accepted on a test network, now evaluates. No depth limit is added; the node has none. Nested constant types, `FlatEncoder`'s term walk and the result term that `CekMachine.Run` and `Evaluator.EvaluateTerm` build still recurse.

## Before

    byte[] bytes = CborWriter.EncodePlutusData(CborReader.DecodePlutusData(Convert.FromHexString("820102"))); // 820102

## After

    byte[] bytes = CborWriter.EncodePlutusData(CborReader.DecodePlutusData(Convert.FromHexString("820102"))); // 9f0102ff, as the node's serialiseData
