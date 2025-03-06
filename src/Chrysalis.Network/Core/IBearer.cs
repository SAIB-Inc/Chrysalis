using System.Buffers;
using System.IO.Pipelines;

namespace Chrysalis.Network.Core;

public interface IBearer : IDisposable
{
    PipeReader Reader { get; }
    PipeWriter Writer { get; }
}