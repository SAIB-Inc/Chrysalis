﻿using Chrysalis.Plutus.Eval;

Console.WriteLine("PlutusVM.Net Transaction Evaluator");
Console.WriteLine("==================================");

string txCborHex = "84a800d90102818258208169cfb0dd7b3686287e4cfff4bc63c883cb4d970c879c07075c30066bd1fbaa000181825839015c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba1a001bc9fd021a0002ba830b58201124502b69ef6917c02db98f8026871e95a70fcc864eb8146bfd44237612b4670dd90102818258208169cfb0dd7b3686287e4cfff4bc63c883cb4d970c879c07075c30066bd1fbaa0110825839015c5c318d01f729e205c95eb1b02d623dd10e78ea58f72d0c13f892b2e8904edc699e2f0ce7b72be7cec991df651a222e2ae9244eb5975cba1a051f966a111a000417c512d9010281825820decc54303906cd11d6edbcaac049baa9959adfee815e1736a4d364094c98e41900a200d90102818258202a60dcffe8ba15307556dbf8d7df142cb9eb15d601251d400d523689d575b83858402d92e12ab3227a7260456f9a4620d5be9b0a05bf7e9750f2a2c92287177adbb019dcbe1da9bf82bff442a1cc79711a5c1dc670386fda1c6fc15c6b3fa868950705a182000082d8799f4568656c6c6fff82194d101a005ee1c6f5f6";
string utxoHex = "82828258208169CFB0DD7B3686287E4CFFF4BC63C883CB4D970C879C07075C30066BD1FBAA00A300581D71FAAE60072C45D121B6E58AE35C624693EE3DAD9EA8ED765EB6F76F9F011A001E8480028201D8184AD8799F4568656C6C6FFF82825820DECC54303906CD11D6EDBCAAC049BAA9959ADFEE815E1736A4D364094C98E41900A300581D71FAAE60072C45D121B6E58AE35C624693EE3DAD9EA8ED765EB6F76F9F011A0018CB2603D81858AD820358A958A701010032323232323225333002323232323253330073370E900118041BAA0011323322533300A3370E900018059BAA00513232533300F30110021533300C3370E900018069BAA00313371E6EB8C040C038DD50039BAE3010300E37546020601C6EA800C5858DD7180780098061BAA00516300C001300C300D001300937540022C6014601600660120046010004601000260086EA8004526136565734AAE7555CF2AB9F5742AE89";

var evaluator = new Evaluator();
var results = evaluator.EvaluateTransaction(txCborHex, utxoHex);

Console.WriteLine("\nEvaluation Results");
for (int i = 0; i < results.Count; i++)
{
    var result = results[i];
    Console.WriteLine($"Redeemer Tag: {result.RedeemerTag}");
    Console.WriteLine($"Index: {result.Index}");
    Console.WriteLine($"ExUnits:");
    Console.WriteLine($"    Memory: {result.ExUnits.Mem}");
    Console.WriteLine($"    Steps: {result.ExUnits.Steps}");
}