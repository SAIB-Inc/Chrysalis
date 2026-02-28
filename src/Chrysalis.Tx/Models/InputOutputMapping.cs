using Chrysalis.Tx.Models.Cbor;

namespace Chrysalis.Tx.Models;

/// <summary>
/// Tracks mappings between input identifiers, their indices, and associated output indices.
/// </summary>
public class InputOutputMapping
{
    private readonly Dictionary<string, (ulong InputIndex, Dictionary<string, ulong> OutputIndexes)> _inputOutputMapping = [];
    private readonly Dictionary<string, ulong> _referenceInputs = [];

    /// <summary>
    /// Adds an input with its index to the mapping.
    /// </summary>
    /// <param name="inputId">The input identifier.</param>
    /// <param name="inputIndex">The input index in the sorted transaction inputs.</param>
    public void AddInput(string inputId, ulong inputIndex)
    {
        ArgumentNullException.ThrowIfNull(inputId);

        if (_inputOutputMapping.TryGetValue(inputId, out (ulong InputIndex, Dictionary<string, ulong> OutputIndexes) existing))
        {
            _inputOutputMapping[inputId] = (inputIndex, existing.OutputIndexes);
        }
        else
        {
            _inputOutputMapping[inputId] = (inputIndex, new Dictionary<string, ulong>());
        }
    }

    /// <summary>
    /// Sets the resolved inputs list, sorted by transaction ID and index.
    /// </summary>
    /// <param name="resolvedInputs">The resolved inputs to store.</param>
    public void SetResolvedInputs(List<ResolvedInput> resolvedInputs)
    {
        ArgumentNullException.ThrowIfNull(resolvedInputs);

        List<ResolvedInput> sortedInputs = [.. resolvedInputs
        .OrderBy(x => Convert.ToHexString(x.Outref.TransactionId.Span))
        .ThenBy(x => x.Outref.Index)];

        ResolvedInputs.AddRange(sortedInputs);
    }

    /// <summary>
    /// Gets the stored resolved inputs.
    /// </summary>
    public List<ResolvedInput> ResolvedInputs { get; } = [];

    /// <summary>
    /// Adds a reference input with its index.
    /// </summary>
    /// <param name="inputId">The reference input identifier.</param>
    /// <param name="inputIndex">The reference input index.</param>
    public void AddReferenceInput(string inputId, ulong inputIndex)
    {
        ArgumentNullException.ThrowIfNull(inputId);
        _referenceInputs[inputId] = inputIndex;
    }

    /// <summary>
    /// Adds an output association to an existing input mapping.
    /// </summary>
    /// <param name="inputId">The input identifier.</param>
    /// <param name="outputId">The output identifier.</param>
    /// <param name="outputIndex">The output index.</param>
    public void AddOutput(string inputId, string outputId, ulong outputIndex)
    {
        ArgumentNullException.ThrowIfNull(inputId);
        ArgumentNullException.ThrowIfNull(outputId);

        if (_inputOutputMapping.TryGetValue(inputId, out (ulong InputIndex, Dictionary<string, ulong> OutputIndexes) existing))
        {
            existing.OutputIndexes[outputId] = outputIndex;
            _inputOutputMapping[inputId] = (existing.InputIndex, existing.OutputIndexes);
        }
        else
        {
            _inputOutputMapping[inputId] = (0, new Dictionary<string, ulong> { { outputId, outputIndex } });
        }
    }

    /// <summary>
    /// Gets the input index and output mappings for a given input identifier.
    /// </summary>
    /// <param name="inputId">The input identifier.</param>
    /// <returns>A tuple of the input index and output index dictionary.</returns>
    public (ulong InputIndex, Dictionary<string, ulong> OutputIndexes) GetInput(string inputId)
    {
        ArgumentNullException.ThrowIfNull(inputId);
        return _inputOutputMapping.TryGetValue(inputId, out (ulong InputIndex, Dictionary<string, ulong> OutputIndexes) value) ? value : (0, new Dictionary<string, ulong>());
    }

    /// <summary>
    /// Gets the index for a reference input identifier.
    /// </summary>
    /// <param name="refInputId">The reference input identifier.</param>
    /// <returns>The reference input index, or 0 if not found.</returns>
    public ulong GetReferenceInput(string refInputId)
    {
        ArgumentNullException.ThrowIfNull(refInputId);
        return _referenceInputs.TryGetValue(refInputId, out ulong value) ? value : 0;
    }

    /// <summary>
    /// Gets a copy of all input/output mappings.
    /// </summary>
    public Dictionary<string, (ulong InputIndex, Dictionary<string, ulong> OutputIndexes)> Mappings =>
        new(_inputOutputMapping);

    /// <summary>
    /// Gets the collection of input identifiers.
    /// </summary>
    public IEnumerable<string> InputIds => _inputOutputMapping.Keys;

    /// <summary>
    /// Checks whether an input identifier exists in the mapping.
    /// </summary>
    /// <param name="inputId">The input identifier to check.</param>
    /// <returns>True if the input exists.</returns>
    public bool HasInput(string inputId)
    {
        ArgumentNullException.ThrowIfNull(inputId);
        return _inputOutputMapping.ContainsKey(inputId);
    }

    /// <summary>
    /// Checks whether an output exists for a given input identifier.
    /// </summary>
    /// <param name="inputId">The input identifier.</param>
    /// <param name="outputId">The output identifier.</param>
    /// <returns>True if the output exists for the input.</returns>
    public bool HasOutput(string inputId, string outputId)
    {
        ArgumentNullException.ThrowIfNull(inputId);
        ArgumentNullException.ThrowIfNull(outputId);
        return _inputOutputMapping.TryGetValue(inputId, out (ulong InputIndex, Dictionary<string, ulong> OutputIndexes) value) && value.OutputIndexes.ContainsKey(outputId);
    }
}
