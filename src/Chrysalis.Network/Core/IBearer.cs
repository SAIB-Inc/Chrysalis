using System.IO.Pipelines;

namespace Chrysalis.Network.Core;

/// <summary>
/// Defines a communication channel that provides bidirectional data transfer capabilities.
/// Implementations typically wrap network connections (TCP, Unix domain sockets, etc.).
/// </summary>
/// <remarks>
/// The IBearer serves as an abstraction layer for different transport mechanisms,
/// providing a consistent interface for reading and writing data through pipes.
/// All implementations must properly manage resources and implement IDisposable.
/// </remarks>
public interface IBearer : IDisposable
{
    /// <summary>
    /// Gets a <see cref="PipeReader"/> for consuming data from the underlying transport.
    /// </summary>
    PipeReader Reader { get; }

    /// <summary>
    /// Gets a <see cref="PipeWriter"/> for sending data to the underlying transport.
    /// </summary>
    PipeWriter Writer { get; }
}