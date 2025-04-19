using Chrysalis.Cbor.Serialization.Attributes;
using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;

namespace Chrysalis.Tx.Cli.Templates.Models;

[CborSerializable]
public partial record PolicyId(byte[] Value) : CborBase;