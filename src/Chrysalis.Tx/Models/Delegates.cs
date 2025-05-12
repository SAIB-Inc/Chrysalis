
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;

namespace Chrysalis.Tx.Models;

public delegate void InputConfig<T>(InputOptions<T> options, T parameter);
public delegate void ReferenceInputConfig<T>(ReferenceInputOptions options, T parameter);
public delegate void OutputConfig<T>(OutputOptions options, T parameter);
public delegate void MintConfig<T>(MintOptions<T> options, T parameter);
public delegate void WithdrawalConfig<T>(WithdrawalOptions<T> options, T parameter);
public delegate IEnumerable<(InputConfig<T> inputConfig, List<ReferenceInputConfig<T>>, List<MintConfig<T>> mintConfigs, List<OutputConfig<T>> outputConfigs)> ConfigGenerator<T>(T parameter);
public delegate Task<Transaction> TransactionTemplate<T>(T parameter);
public delegate void PreBuildHook<T>(TransactionBuilder builder, InputOutputMapping mapping, T parameter);