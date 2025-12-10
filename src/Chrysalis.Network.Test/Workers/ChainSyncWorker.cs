using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Chrysalis.Network.Multiplexer;
using Chrysalis.Network.MiniProtocols;
using Chrysalis.Network.Cbor.ChainSync;
using Chrysalis.Network.Cbor.Common;
using System.Diagnostics;

namespace Chrysalis.Network.Test.Workers;

/// <summary>
/// Background worker that continuously syncs with the Cardano blockchain.
/// </summary>
public class ChainSyncWorker(ILogger<ChainSyncWorker> logger, IConfiguration configuration) : BackgroundService
{
    private NodeClient? _nodeClient;
    
    // Metrics
    private long _blocksProcessed;
    private long _rollBacksProcessed;
    private DateTime _lastBlockTime = DateTime.UtcNow;
    private ulong _currentSlot;
    private int _currentBlockNumber;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("ChainSync Worker starting...");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunChainSyncAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation("ChainSync Worker shutting down gracefully");
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ChainSync error occurred, retrying in 5 seconds...");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            finally
            {
                _nodeClient?.Dispose();
                _nodeClient = null;
            }
        }
    }

    private async Task RunChainSyncAsync(CancellationToken cancellationToken)
    {
        // Get configuration from root
        string connectionType = configuration.GetValue<string>("ConnectionType") ?? "UnixSocket";
        ulong networkMagic = configuration.GetValue<ulong>("NetworkMagic", 2);
        
        // Connect based on connection type
        if (connectionType.Equals("Tcp", StringComparison.OrdinalIgnoreCase))
        {
            string tcpHost = configuration.GetValue<string>("TcpHost") ?? "localhost";
            int tcpPort = configuration.GetValue<int>("TcpPort", 3001);
            logger.LogInformation("Connecting to Cardano node via TCP: {Host}:{Port}", tcpHost, tcpPort);
            _nodeClient = await NodeClient.ConnectTcpAsync(tcpHost, tcpPort, cancellationToken);
        }
        else
        {
            string socketPath = configuration.GetValue<string>("SocketPath") ?? "/tmp/preview-node.socket";
            logger.LogInformation("Connecting to Cardano node via Unix socket: {SocketPath}", socketPath);
            _nodeClient = await NodeClient.ConnectAsync(socketPath, cancellationToken);
        }

        await _nodeClient.StartAsync(networkMagic);
        
        logger.LogInformation("Successfully connected and completed handshake");

        // Find intersection with the chain
        await FindIntersectionAsync(_nodeClient.ChainSync, cancellationToken);
        
        // Start continuous sync
        await ContinuousSyncAsync(_nodeClient.ChainSync, cancellationToken);
    }

    private async Task FindIntersectionAsync(ChainSync chainSync, CancellationToken cancellationToken)
    {
        logger.LogInformation("Finding intersection point with chain...");

        // Get starting point from root configuration with new defaults
        ulong slot = configuration.GetValue<ulong>("Slot", 89722582);
        string hashHex = configuration.GetValue<string>("Hash") ?? "cb09754fb3d1436c25f3280b91b82882da93dc71c69eb256c79a66b8ea7273a3";

        byte[] hash = string.IsNullOrEmpty(hashHex) 
            ? Array.Empty<byte>() 
            : Convert.FromHexString(hashHex);

        List<Point> points = new List<Point> { new Point(slot, hash) };
        
        logger.LogInformation("Starting from slot {Slot}, hash {Hash}", 
            slot, 
            string.IsNullOrEmpty(hashHex) ? "genesis" : hashHex);

        ChainSyncMessage intersectResponse = await chainSync.FindIntersectionAsync(points, cancellationToken);
        
        switch (intersectResponse)
        {
            case MessageIntersectFound found:
                logger.LogInformation("Found intersection at slot {Slot}, block {Block}",
                    found.Point.Slot, found.Tip.BlockNumber);
                _currentSlot = found.Point.Slot;
                _currentBlockNumber = found.Tip.BlockNumber ?? 0;
                break;

            case MessageIntersectNotFound notFound:
                logger.LogInformation("No intersection found, starting from tip at block {Block}",
                    notFound.Tip.BlockNumber);
                _currentBlockNumber = notFound.Tip.BlockNumber ?? 0;
                break;

            default:
                throw new InvalidOperationException($"Unexpected intersection response: {intersectResponse}");
        }
    }

    private async Task ContinuousSyncAsync(ChainSync chainSync, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting continuous chain synchronization...");
        Stopwatch logTimer = Stopwatch.StartNew();

        while (!cancellationToken.IsCancellationRequested && !chainSync.IsDone)
        {
            try
            {
                // Check if multiplexer is healthy before attempting operations
                if (_nodeClient != null && !_nodeClient.IsPlexerHealthy())
                {
                    Exception? plexerException = _nodeClient.GetPlexerException();
                    logger.LogError(plexerException, "Multiplexer has stopped - connection lost");
                    throw new InvalidOperationException("Connection lost: multiplexer has stopped", plexerException);
                }

                MessageNextResponse? response = await chainSync.NextRequestAsync(cancellationToken);

                switch (response)
                {
                    case MessageRollForward rollForward:
                        await ProcessRollForwardAsync(rollForward);
                        break;
                        
                    case MessageRollBackward rollBackward:
                        await ProcessRollBackwardAsync(rollBackward);
                        break;
                        
                    case MessageAwaitReply:
                        logger.LogInformation("Awaiting next block from node...");
                        break;
                        
                    case null:
                        logger.LogInformation("Received null response from chain sync");
                        break;
                }
                
                // Log metrics periodically
                if (logTimer.Elapsed > TimeSpan.FromSeconds(10))
                {
                    LogMetrics();
                    logTimer.Restart();
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Error processing chain sync response");
                throw;
            }
        }
        
        LogMetrics();
    }

    private Task ProcessRollForwardAsync(MessageRollForward rollForward)
    {
        _blocksProcessed++;
        _lastBlockTime = DateTime.UtcNow;
        _currentSlot = rollForward.Tip.Slot.Slot;
        _currentBlockNumber = rollForward.Tip.BlockNumber ?? 0;
        string blockHash = Convert.ToHexString(rollForward.Tip.Slot.Hash).ToLowerInvariant();

        logger.LogInformation(
            "Found block: Slot {Slot}, Hash {Hash}",
            _currentSlot,
            blockHash);

        return Task.CompletedTask;
    }

    private Task ProcessRollBackwardAsync(MessageRollBackward rollBackward)
    {
        _rollBacksProcessed++;
        _currentSlot = rollBackward.Point.Slot;

        logger.LogInformation(
            "Chain rollback to slot {Slot}, new tip at block {BlockNumber}",
            rollBackward.Point.Slot,
            rollBackward.Tip.BlockNumber);

        _currentBlockNumber = rollBackward.Tip.BlockNumber ?? 0;

        return Task.CompletedTask;
    }

    private void LogMetrics()
    {
        logger.LogInformation(
            "Processed {Blocks:N0} blocks, {Rollbacks} rollbacks | Current block: {BlockNumber} | Slot: {Slot}",
            _blocksProcessed,
            _rollBacksProcessed,
            _currentBlockNumber,
            _currentSlot);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("ChainSync Worker stopping...");
        
        if (_nodeClient?.ChainSync != null && !_nodeClient.ChainSync.IsDone)
        {
            try
            {
                await _nodeClient.ChainSync.DoneAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error sending done message");
            }
        }
        
        await base.StopAsync(cancellationToken);
        
        logger.LogInformation(
            "ChainSync Worker stopped. Total blocks processed: {Blocks:N0}, Total rollbacks: {Rollbacks}",
            _blocksProcessed,
            _rollBacksProcessed);
    }
}