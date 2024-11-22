using Chrysalis.Cardano.Core;

namespace Chrysalis.Builders.Core;

public class TransactionBuilder : BuilderBase<Transaction>
{
    /// <summary>
    /// Sets the transaction body from an instance of <see cref="TransactionBody"/>.
    /// </summary>
    public void SetTransactionBody(TransactionBody transactionBody)
    {
        Instance.TransactionBody = transactionBody;
    }

    /// <summary>
    /// Sets the transaction body from an instance of <see cref="TransactionBodyBuilder"/>.
    /// </summary>
    public void SetTransactionBody(TransactionBodyBuilder transactionBodyBuilder)
    {
        Instance.TransactionBody = transactionBodyBuilder.Build();
    }

    /// <summary>
    /// Sets the transaction witness set from an instance of <see cref="TransactionWitnessSet"/>.
    /// </summary>
    public void SetTransactionWitnessSet(TransactionWitnessSet transactionWitnessSet)
    {
        Instance.TransactionWitnessSet = transactionWitnessSet;
    }

    /// <summary>
    /// Sets the transaction witness set from an instance of <see cref="TransactionWitnessSetBuilder"/>.
    /// </summary>
    public void SetTransactionWitnessSet(TransactionWitnessSetBuilder transactionWitnessSetBuilder)
    {
        Instance.TransactionWitnessSet = transactionWitnessSetBuilder.Build();
    }

    /// <summary>
    /// Sets the auxiliary data from an instance of <see cref="AuxiliaryData"/>.
    /// </summary>
    public void SetAuxiliaryData(AuxiliaryData auxiliaryData)
    {
        Instance.AuxiliaryData = new CborNullable<AuxiliaryData>(auxiliaryData);
    }

    /// <summary>
    /// Sets the auxiliary data from an instance of <see cref="AuxiliaryDataBuilder"/>.
    /// </summary>
    public void SetAuxiliaryData(AuxiliaryDataBuilder auxiliaryDataBuilder)
    {
        Instance.AuxiliaryData = auxiliaryDataBuilder.Build();
    }

    /// <summary>
    /// Calculates the fee for the transaction.
    /// </summary>
    /// <returns>The fee for the transaction.</returns>
    /// <exception cref="NotImplementedException">This method is not implemented.</exception>
    public ulong CalculateFee()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Sets the fee for the transaction.
    /// </summary>
    /// <param name="fee">The fee for the transaction.</param>
    /// <exception cref="NotImplementedException">This method is not implemented.</exception>
    public void SetFee(ulong fee)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Calculates and sets the fee for the transaction.
    /// </summary>
    /// <exception cref="NotImplementedException">This method is not implemented.</exception>
    public void CalculateAndSetFee()
    {
        ulong fee = CalculateFee();
        SetFee(fee);
    }

    /// <summary>
    /// Simulates the transaction to determine the execution units and sets the execution units.
    /// </summary>
    /// <exception cref="NotImplementedException">This method is not implemented.</exception>
    public void SetExUnits()
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// Builds the transaction.
    /// </summary>
    /// <returns>The built transaction.</returns>
    /// <exception cref="NotImplementedException">This method is not implemented.</exception>
    public override Transaction Build()
    {
        // This may contain additional logics like setting the fee, execution units, if not set.
        throw new NotImplementedException();
    }
}