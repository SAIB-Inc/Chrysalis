# Known issues

## Self-containing user Plutus data types

A Plutus data type you define that contains itself directly — for example, a blueprint tree (`Leaf | Node(Tree, Tree)`) or a linked chain — is read, written, compared and printed one call level per nesting level. Very deep data of that shape can crash the process with a stack overflow (on a default 1.5 MB .NET secondary thread on macOS or Windows, at about 1,300 levels).

Reading types that nest through a list (`CborList<T>`) or an option (`ICborOption<T>`) is not affected, nor is reading Plutus data with the codec's `IPlutusData`. Writing a deep value built in code (for example, with `new` records and `CborList.Create`) still takes one call level per nesting level and can overflow. To read untrusted data of unknown depth safely, decode it as `IPlutusData` or place the recursion behind a list or option.

A fix is planned for this release. <!-- X47 -->
