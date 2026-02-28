
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Tx.Builders;

namespace Chrysalis.Tx.Models;

/// <summary>
/// Configures input selection for a transaction template.
/// </summary>
/// <typeparam name="T">The transaction parameter type.</typeparam>
/// <param name="options">The input options to configure.</param>
/// <param name="parameter">The transaction parameters.</param>
public delegate void InputConfig<T>(InputOptions<T> options, T parameter);

/// <summary>
/// Configures reference input selection for a transaction template.
/// </summary>
/// <typeparam name="T">The transaction parameter type.</typeparam>
/// <param name="options">The reference input options to configure.</param>
/// <param name="parameter">The transaction parameters.</param>
public delegate void ReferenceInputConfig<T>(ReferenceInputOptions options, T parameter);

/// <summary>
/// Configures output creation for a transaction template.
/// </summary>
/// <typeparam name="T">The transaction parameter type.</typeparam>
/// <param name="options">The output options to configure.</param>
/// <param name="parameter">The transaction parameters.</param>
/// <param name="fee">The current estimated fee.</param>
public delegate void OutputConfig<T>(OutputOptions options, T parameter, ulong fee);

/// <summary>
/// Configures minting operations for a transaction template.
/// </summary>
/// <typeparam name="T">The transaction parameter type.</typeparam>
/// <param name="options">The mint options to configure.</param>
/// <param name="parameter">The transaction parameters.</param>
public delegate void MintConfig<T>(MintOptions<T> options, T parameter);

/// <summary>
/// Configures withdrawal operations for a transaction template.
/// </summary>
/// <typeparam name="T">The transaction parameter type.</typeparam>
/// <param name="options">The withdrawal options to configure.</param>
/// <param name="parameter">The transaction parameters.</param>
public delegate void WithdrawalConfig<T>(WithdrawalOptions<T> options, T parameter);

/// <summary>
/// Builds a complete transaction from the given parameters.
/// </summary>
/// <typeparam name="T">The transaction parameter type.</typeparam>
/// <param name="parameter">The transaction parameters.</param>
/// <returns>The built transaction.</returns>
public delegate Task<Transaction> TransactionTemplate<T>(T parameter);

/// <summary>
/// Hook invoked before final transaction build for custom modifications.
/// </summary>
/// <typeparam name="T">The transaction parameter type.</typeparam>
/// <param name="builder">The transaction builder.</param>
/// <param name="mapping">The input/output mapping.</param>
/// <param name="parameter">The transaction parameters.</param>
public delegate void PreBuildHook<T>(TransactionBuilder builder, InputOutputMapping mapping, T parameter);

/// <summary>
/// Builds a native script from the given parameters.
/// </summary>
/// <typeparam name="T">The transaction parameter type.</typeparam>
/// <param name="parameter">The transaction parameters.</param>
/// <returns>The constructed native script.</returns>
public delegate NativeScript NativeScriptBuilder<T>(T parameter);

/// <summary>
/// Builds transaction metadata from the given parameters.
/// </summary>
/// <typeparam name="T">The transaction parameter type.</typeparam>
/// <param name="parameter">The transaction parameters.</param>
/// <returns>The constructed metadata.</returns>
public delegate Metadata MetadataConfig<T>(T parameter);
