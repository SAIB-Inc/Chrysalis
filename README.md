# Chrysalis: Cardano Serialization Library for .NET 🦋

[![.NET](https://github.com/0xAccretion/Chrysalis/actions/workflows/dotnet.yml/badge.svg)](https://github.com/0xAccretion/Chrysalis/actions/workflows/dotnet.yml)
![License](https://img.shields.io/badge/License-MIT-blue.svg)
![C#](https://img.shields.io/badge/C%23-purple.svg)
![Language](https://img.shields.io/github/languages/top/0xAccretion/Chrysalis.svg)


Chrysalis is an open-source .NET library designed to facilitate the serialization and deserialization of Cardano blockchain data structures. With a strong focus on adhering to the Cardano standards and enhancing the .NET Cardano developer ecosystem, Chrysalis aims to provide developers with a reliable and consistent toolkit for working with Cardano.

🚧 **NOTE:** This library is currently a work in progress. Feedback and contributions are welcome!

## Features

- **Cardano Serialization**: Convert Cardano blockchain data structures to and from CBOR (Concise Binary Object Representation), allowing seamless and efficient data exchanges.
- **Bech32 Address Encoding/Decoding**: Simplifies the encoding and decoding of Cardano addresses, ensuring compatibility with widely used formats.This allows you to handle Cardano addresses seamlessly.
- **Extensive Data Model Support**: Work with a wide range of Cardano data types, including Transactions, Assets, MultiAssets, and more.
- **Smart Contract Interaction**: Interact with Cardano smart contracts.
- **Cross-Platform Compatibility**: Use Chrysalis in any .NET project, including .NET Core, .NET Framework, Xamarin, and more.


## Roadmap 🚀

1. **(De)serialization Support**: Achieve complete serialization and deserialization for any Cardano data type described in CDDL https://github.com/input-output-hk/cardano-ledger/blob/master/eras/alonzo/test-suite/cddl-files/alonzo.cddl.
2. **Transaction Handling**: Introduce capabilities for building and signing Cardano transactions.
3. **Advanced Address Management**: Implement address generation, derivation, and other associated functionalities.

## Getting Started

To use Chrysalis in your .NET project:

1. You can install Chrysalis via NuGet:
    `dotnet add package Chrysalis`

2. Example Usage
    
    CBOR (De)serialization
    ```csharp
    var originalTransaction = CborSerializer.FromHex<Transaction>(originalTransactionCborHex)!;

    var serializedTransaction = CborSerializer.Serialize(originalTransaction);

    var deserializedTransaction = CborSerializer.Deserialize<Transaction>(serializedTransaction);
    ```

    Block Deserialization and Serialization
    ```csharp
        byte[] serializedBlock = CborSerializer.Serialize(originalBlock);
        Chrysalis.Cardano.Models.Core.Block.Block deserializedBlock = CborSerializer.Deserialize(serializedBlock);
    ```

    Access Deserialized Transactions, Transaction Inputs, and Transaction Outputs from a Deserialized Block
    ```csharp
        IEnumerable<TransactionBody> transactions = originalBlock.TransactionBodies();
        foreach (TransactionBody tx in transactions)
        {
            IEnumerable<TransactionInput> inputs = tx.Inputs();
            IEnumerable<TransactionOutput> outputs = tx.Outputs();    
        }
    ```

    Access a Transaction Input's Transaction Id and Index
    ```csharp
        foreach (TransactionInput input in tx.Inputs())
        {
            string id = input.TransacationId();
            ulong index = input.Index();
        }
    ```

    Access a Transaction Output's Address and Balance
    ```csharp
        foreach (TransactionOutput output in tx.Outputs())
        {
            string addr = output.Address().Value.ToBech32();
            Value balance = output.Amount();
            Value multiasset = balance.MultiAsset();
            ulong lovelace = balance.LoveLace();
        }
    ```

    Serialize Transactions in a Block
    ```csharp
        for (uint x = 0; x < transactions.Count(); x++)
        {
            CborSerializer.Serialize(transactions.ElementAt((int)x))
        }
    ```

    Bech32 Address Encoding/Decoding
    ```csharp
    var addressBech32 = "addr...";
    var addressObject = Address.FromBech32(addressBech32);
    var addressBech32Again = addressObject.ToBech32();
    var paymentKeyHash = addressObject.GetPaymentKeyHash();
    var stakeKeyHash = addressObject.GetStakeKeyHash();
    ```


## How to Contribute

Interested in contributing to Chrysalis? Great! We appreciate any help, be it in the form of code contributions, documentation, or even bug reports.

- **Fork and Clone**: Fork this repository, clone it locally, and set up the necessary development environment.
- **Branch**: Always create a new branch for your work.
- **Pull Request**: Submit a pull request once you're ready. Ensure you describe your changes clearly.
- **Feedback**: Wait for feedback and address any comments or suggestions.

## License

MIT License

Copyright (c) 2023 0xAccretion

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

---

Give your feedback, star the repository if you found it useful, and consider contributing to push the Cardano .NET ecosystem forward! 🌟

