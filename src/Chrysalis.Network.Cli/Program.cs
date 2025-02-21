// using System.Net.Sockets;

// TcpClient client = new();
// client.Connect("localhost", 1234);

// var streamWriter = new StreamWriter(client.GetStream());
// var streamReader = new StreamReader(client.GetStream());

// while (true)
// {
//     Console.Write("Enter message: ");
//     var message = Console.ReadLine();
//     await streamWriter.WriteLineAsync(message);
//     await streamWriter.FlushAsync();
//     message = await streamReader.ReadLineAsync();
//     Console.WriteLine("Reply: " + message);
// }

using System.Net;
using System.Net.Sockets;
using System.Text;

var listener = new TcpListener(IPAddress.Loopback, 1234);
listener.Start();
Console.WriteLine("Server listening on port 1234...");

while (true)
{
    var client = await listener.AcceptTcpClientAsync();
    Console.WriteLine("Client connected.");

    var networkStream = client.GetStream();
    var streamReader = new StreamReader(networkStream, Encoding.UTF8);
    var streamWriter = new StreamWriter(networkStream, Encoding.UTF8);

    while (true)
    {
        var message = await streamReader.ReadLineAsync();
        if (message == null)
            break;

        Console.WriteLine("Received: " + message);

        await streamWriter.WriteLineAsync(message);  // Echo back the received message
        await streamWriter.FlushAsync();
    }

    client.Close();
    Console.WriteLine("Client disconnected.");
}