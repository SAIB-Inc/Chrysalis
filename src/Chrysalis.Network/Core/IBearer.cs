using System.Buffers;
using System.IO.Pipelines;

namespace Chrysalis.Network.Core;

/// <summary>
/// Represents an abstract network bearer using functional asynchronous effects.
/// </summary>
public interface IBearer : IDisposable
{
    PipeReader Reader { get; }
    PipeWriter Writer { get; }
}