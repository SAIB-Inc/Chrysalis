using Dapper;
using Npgsql;

namespace Chrysalis.Network.Cli;

public static class BlockDbHelper
{
    private static readonly string ConnectionString = "Host=localhost;Database=chrysalis-cs;Username=postgres;Password=test1234;Port=5432;Include Error Detail=true;";

    public static async Task InitializeDbAsync()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        // Drop the table if it exists
        await connection.ExecuteAsync("DROP TABLE IF EXISTS blocks");

        // Create a fresh table with minimal columns
        await connection.ExecuteAsync(@"
            CREATE TABLE blocks (
                block_number BIGINT NOT NULL,
                block_slot BIGINT NOT NULL,
                block_hash VARCHAR(64) NOT NULL,
                timestamp TIMESTAMP NOT NULL DEFAULT NOW()
            )");

        Console.WriteLine("Database table cleared and recreated");
    }

    public static async Task InsertBlockAsync(ulong blockNumber, ulong blockSlot, string blockHash)
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO blocks (block_number, block_slot, block_hash) VALUES (@BlockNumber, @BlockSlot, @BlockHash)",
            new
            {
                BlockNumber = (long)blockNumber,
                BlockSlot = (long)blockSlot,
                BlockHash = blockHash
            });
    }
}