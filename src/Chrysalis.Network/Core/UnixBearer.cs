using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using SocketProtocolType = System.Net.Sockets.ProtocolType;

namespace Chrysalis.Network.Core;

public class UnixBearer : IBearer
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly PipeReader _reader;
    private readonly PipeWriter _writer;

    private UnixBearer(Socket socket, NetworkStream stream, PipeReader reader, PipeWriter writer)
    {

        _socket = socket;
        _stream = stream;
        _reader = reader;
        _writer = writer;
    }

    public static async Task<UnixBearer> CreateAsync(string path)
    {
        // Create a Unix domain socket.
        Socket socket = new(AddressFamily.Unix, SocketType.Stream, SocketProtocolType.Unspecified);
        UnixDomainSocketEndPoint endpoint = new(path);
        await socket.ConnectAsync(endpoint);
        NetworkStream stream = new(socket, ownsSocket: true);
        PipeReader reader = PipeReader.Create(stream);
        PipeWriter writer = PipeWriter.Create(stream);

        return new UnixBearer(socket, stream, reader, writer);
    }

    public async Task<ReadOnlySequence<byte>> ReceiveExactAsync(int len, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            ReadResult result = await _reader.ReadAsync(cancellationToken);

            if (result.IsCanceled) throw new OperationCanceledException();

            ReadOnlySequence<byte> buffer = result.Buffer;

            if (buffer.Length >= len)
            {
                buffer = buffer.Slice(0, len);
                _reader.AdvanceTo(buffer.Start, buffer.End);
                return buffer;
            }

            if (result.IsCompleted)
                throw new EndOfStreamException("Connection closed before receiving the required number of bytes");
        }

        throw new OperationCanceledException(cancellationToken);
    }

    public async Task SendAsync(ReadOnlySequence<byte> data, CancellationToken cancellationToken)
    {
        foreach (ReadOnlyMemory<byte> segment in data)
        {
            Memory<byte> memory = _writer.GetMemory(segment.Length);
            segment.Span.CopyTo(memory.Span);
            _writer.Advance(segment.Length);
        }

        await _writer.FlushAsync(cancellationToken);
    }

    public void Dispose()
    {
        _stream.Dispose();
        _socket.Dispose();
        GC.SuppressFinalize(this);
    }
}

// using System;
// using System.Buffers;
// using System.IO.Pipelines;
// using System.Net.Sockets;
// using System.Threading;
// using System.Threading.Tasks;
// using LanguageExt;
// using static LanguageExt.Prelude;
// using SocketProtocolType = System.Net.Sockets.ProtocolType;

// namespace Chrysalis.Network.Core;
// /// <summary>
// /// Represents a Unix domain socket–based implementation of the IBearer interface using functional asynchronous effects.
// /// </summary>
// public class UnixBearer : IBearer
// {
//     private readonly Socket _socket;
//     private readonly NetworkStream _stream;
//     private readonly PipeReader _reader;
//     private readonly PipeWriter _writer;

//     //     /// <summary>
//     //     /// Private constructor to enforce factory method usage.
//     //     /// </summary>
//     //     /// <param name="socket">The Unix domain socket instance.</param>
//     //     /// <param name="stream">The network stream associated with the socket.</param>
// private UnixBearer(Socket socket, NetworkStream stream, PipeReader reader, PipeWriter writer)
// {
//     _socket = socket;
//     _stream = stream;
//     _reader = reader;
//     _writer = writer;
// }

//     //     /// <summary>
//     //     /// Asynchronous factory method to create a UnixBearer instance.
//     //     /// Returns an Aff monad encapsulating the asynchronous connection effect.
//     //     /// </summary>
//     //     /// <param name="path">The Unix domain socket path.</param>
//     //     /// <returns>An Aff monad wrapping a UnixBearer instance.</returns>
//     // public static Aff<UnixBearer> Create(string path) =>
//     //     Aff(async () =>
//     //     {
// // Create a Unix domain socket.
// var socket = new Socket(AddressFamily.Unix, SocketType.Stream, SocketProtocolType.Unspecified);
// var endpoint = new UnixDomainSocketEndPoint(path);
// await socket.ConnectAsync(endpoint);
// // Create a network stream over the socket.
// var stream = new NetworkStream(socket, ownsSocket: true);
// return new UnixBearer(socket, stream);
//     //     });

//     //     /// <summary>
//     //     /// Sends data asynchronously over the network stream.
//     //     /// Returns an Aff monad that encapsulates the effect.
//     //     /// </summary>
//     //     /// <param name="data">The byte array to send.</param>
//     //     /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
//     //     /// <returns>An Aff monad yielding Unit upon completion.</returns>
//     //     public Aff<Unit> Send(byte[] data, CancellationToken cancellationToken) =>
//     //         Aff(async () =>
//     //         {
//     //             await _stream.WriteAsync(data, cancellationToken);
//     //             return unit;
//     //         });

//     //     /// <summary>
//     //     /// Receives exactly the specified number of bytes from the network stream asynchronously.
//     //     /// Returns an Aff monad that encapsulates the effect.
//     //     /// </summary>
//     //     /// <param name="len">The exact number of bytes to receive.</param>
//     //     /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
//     //     /// <returns>An Aff monad yielding the received byte array.</returns>
//     //     public Aff<byte[]> ReceiveExact(int len, CancellationToken cancellationToken) =>
//     //         Aff(async () =>
//     //         {
//     //             if (_stream.DataAvailable)
//     //             {
//     //                 byte[] buffer = new byte[len];
//     //                 await _stream.ReadExactlyAsync(buffer, 0, len, cancellationToken);
//     //                 return buffer;
//     //             }
//     //             return [];
//     //         });

//     //     /// <summary>
//     //     /// Disposes the UnixBearer by releasing the network stream and socket resources.
//     //     /// </summary>
//     //     public void Dispose()
//     //     {
//     // _stream.Dispose();
//     // _socket.Dispose();
//     // GC.SuppressFinalize(this);
//     //     }
//     // }

//     public static async Task<UnixBearer> Create(string path)
//     {
//         // Create a Unix domain socket.
//         Socket socket = new(AddressFamily.Unix, SocketType.Stream, SocketProtocolType.Unspecified);
//         UnixDomainSocketEndPoint endpoint = new(path);
//         await socket.ConnectAsync(endpoint);

//         NetworkStream stream = new(socket, ownsSocket: true);
//         PipeReader pipeReader = PipeReader.Create(stream);
//         PipeWriter pipeWriter = PipeWriter.Create(stream);
//         return new UnixBearer(socket, stream, pipeReader, pipeWriter);
//     }

//     public async Task ReceiveExact(int len, Func<ReadOnlySequence<byte>, Task> processFunc, CancellationToken cancellationToken)
//     {
//         while (true)
//         {
//             ReadResult result = await _reader.ReadAsync(cancellationToken);

//             if (result.IsCanceled) throw new OperationCanceledException();

//             ReadOnlySequence<byte> buffer = result.Buffer;

//             if (buffer.Length >= len)
//             {
//                 ReadOnlySequence<byte> slice = buffer.Slice(0, len);
//                 await processFunc(slice);
//             }

//             _reader.AdvanceTo(buffer.Start, buffer.End);

//             if (result.IsCompleted)
//                 throw new EndOfStreamException("Connection closed before receiving the required number of bytes");
//         }
//     }

//     public async Task Send(ReadOnlySequence<byte> data, CancellationToken cancellationToken)
//     {
//         foreach (ReadOnlyMemory<byte> segment in data)
//         {
//             Memory<byte> memory = _writer.GetMemory(segment.Length);
//             segment.Span.CopyTo(memory.Span);
//             _writer.Advance(segment.Length);
//         }

//         await _writer.FlushAsync(cancellationToken);
//     }

//     public void Dispose()
//     {
//         _stream.Dispose();
//         _socket.Dispose();
//         GC.SuppressFinalize(this);
//     }
// }
