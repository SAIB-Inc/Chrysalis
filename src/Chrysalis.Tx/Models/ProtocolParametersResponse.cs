namespace Chrysalis.Tx.Models;

/// <summary>
/// Represents a complete protocol parameters response with all Cardano protocol parameters.
/// </summary>
/// <param name="Epoch">The epoch number.</param>
/// <param name="MinFeeA">The minimum fee coefficient A.</param>
/// <param name="MinFeeB">The minimum fee constant B.</param>
/// <param name="MaxBlockSize">The maximum block size in bytes.</param>
/// <param name="MaxTxSize">The maximum transaction size in bytes.</param>
/// <param name="MaxBlockHeaderSize">The maximum block header size.</param>
/// <param name="KeyDeposit">The key registration deposit.</param>
/// <param name="PoolDeposit">The pool registration deposit.</param>
/// <param name="EMax">The maximum epoch.</param>
/// <param name="NOpt">The desired number of pools.</param>
/// <param name="A0">The pool pledge influence factor.</param>
/// <param name="Rho">The monetary expansion rate.</param>
/// <param name="Tau">The treasury growth rate.</param>
/// <param name="DecentralisationParam">The decentralisation parameter.</param>
/// <param name="ExtraEntropy">Extra entropy, if any.</param>
/// <param name="ProtocolMajorVer">The protocol major version.</param>
/// <param name="ProtocolMinorVer">The protocol minor version.</param>
/// <param name="MinUtxo">The minimum UTxO value.</param>
/// <param name="MinPoolCost">The minimum pool cost.</param>
/// <param name="Nonce">The nonce.</param>
/// <param name="CostModels">The cost models.</param>
/// <param name="CostModelsRaw">The raw cost models.</param>
/// <param name="PriceMem">The memory unit price.</param>
/// <param name="PriceStep">The step unit price.</param>
/// <param name="MaxTxExMem">The maximum transaction execution memory.</param>
/// <param name="MaxTxExSteps">The maximum transaction execution steps.</param>
/// <param name="MaxBlockExMem">The maximum block execution memory.</param>
/// <param name="MaxBlockExSteps">The maximum block execution steps.</param>
/// <param name="MaxValSize">The maximum value size.</param>
/// <param name="CollateralPercent">The collateral percentage.</param>
/// <param name="MaxCollateralInputs">The maximum collateral inputs.</param>
/// <param name="CoinsPerUtxoSize">The coins per UTxO byte size.</param>
/// <param name="MinFeeRefScriptCostPerByte">The minimum fee for reference script cost per byte.</param>
public record ProtocolParametersResponse(
    int Epoch,
    int MinFeeA,
    int MinFeeB,
    int MaxBlockSize,
    int MaxTxSize,
    int MaxBlockHeaderSize,
    string KeyDeposit,
    string PoolDeposit,
    int EMax,
    int NOpt,
    double A0,
    double Rho,
    double Tau,
    double DecentralisationParam,
    object? ExtraEntropy,
    int ProtocolMajorVer,
    int ProtocolMinorVer,
    string MinUtxo,
    string MinPoolCost,
    string Nonce,
    Dictionary<string, Dictionary<string, int>>? CostModels,
    Dictionary<string, int[]>? CostModelsRaw,
    double PriceMem,
    double PriceStep,
    string MaxTxExMem,
    string MaxTxExSteps,
    string MaxBlockExMem,
    string MaxBlockExSteps,
    string MaxValSize,
    int CollateralPercent,
    int MaxCollateralInputs,
    string CoinsPerUtxoSize,
    int MinFeeRefScriptCostPerByte
);
