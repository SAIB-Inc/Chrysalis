using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core;

if (Console.IsInputRedirected)
{
    using Stream inputStream = Console.OpenStandardInput();
    byte[] buffer = new byte[1024];

    while (true)
    {
        // Read the 4-byte length prefix
        byte[] lengthBytes = new byte[4];
        int bytesRead = inputStream.Read(lengthBytes, 0, 4);

        // Check if we’ve reached the end of the stream or have an incomplete length
        if (bytesRead < 4)
        {
            if (bytesRead > 0)
            {
                Console.Error.WriteLine("Incomplete length read. Exiting.");
            }
            break;
        }

        // Convert big-endian length to integer
        int blockSize = BitConverter.ToInt32([.. lengthBytes.Reverse()], 0);

        int totalBlockBytesRead = 0;
        byte[] blockBuffer = new byte[blockSize];

        // Read the block data in chunks
        while (totalBlockBytesRead < blockSize)
        {
            int bytesToRead = Math.Min(buffer.Length, blockSize - totalBlockBytesRead);
            bytesRead = inputStream.Read(buffer, 0, bytesToRead);

            if (bytesRead == 0)
            {
                Console.Error.WriteLine("Unexpected end of stream while reading block.");
                return;
            }

            // Copy the read bytes to the block buffer
            Buffer.BlockCopy(buffer, 0, blockBuffer, totalBlockBytesRead, bytesRead);

            totalBlockBytesRead += bytesRead;
        }

        //Console.Out.WriteLine(Convert.ToHexString(blockBuffer));
        _ = CborSerializer.Deserialize<BlockWithEra>(blockBuffer);
        //Console.Out.WriteLine(block.Block.Slot());
    }
}