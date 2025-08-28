using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Chrysalis.Network.Test.Workers;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

// Configure logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Register services
builder.Services.AddHostedService<ChainSyncWorker>();

// Build and run
IHost host = builder.Build();

ILogger<Program> logger = host.Services.GetRequiredService<ILogger<Program>>();
IConfiguration config = host.Services.GetRequiredService<IConfiguration>();

// Log startup configuration
logger.LogInformation("Starting Chrysalis ChainSync Test Worker");

string connectionType = config.GetValue<string>("ConnectionType") ?? "UnixSocket";

string connectionInfo = connectionType.Equals("Tcp", StringComparison.OrdinalIgnoreCase)
    ? $"TCP {config.GetValue<string>("TcpHost") ?? "localhost"}:{config.GetValue<int>("TcpPort", 3001)}"
    : $"Unix Socket {config.GetValue<string>("SocketPath") ?? "/tmp/preview-node.socket"}";

logger.LogInformation("Connection Type: {ConnectionType}, Target: {Config}", connectionType, connectionInfo);
logger.LogInformation("Network Magic: {NetworkMagic}", config.GetValue<ulong>("NetworkMagic", 2));
logger.LogInformation("Starting Point: Slot {Slot}, Hash {Hash}", 
    config.GetValue<ulong>("Slot", 89722582),
    config.GetValue<string>("Hash") ?? "cb09754fb3d1436c25f3280b91b82882da93dc71c69eb256c79a66b8ea7273a3");

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Host terminated unexpectedly");
    Environment.Exit(1);
}