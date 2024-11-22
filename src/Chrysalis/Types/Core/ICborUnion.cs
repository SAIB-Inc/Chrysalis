using Chrysalis.Attributes;
using Chrysalis.Converters;

namespace Chrysalis.Types.Core;

[CborSerializable(Converter = typeof(CborUnionConverter))]
public interface ICborUnion : ICbor;