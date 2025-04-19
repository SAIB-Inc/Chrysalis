﻿using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Plutus.Address;
using Chrysalis.Tx.Cli.Templates;
using Chrysalis.Tx.Cli.Templates.Models;
using Chrysalis.Tx.Cli.Templates.Parameters;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Providers;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Words;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;
using PlutusCredential = Chrysalis.Cbor.Types.Plutus.Address.Credential;
using Chrysalis.Cbor.Serialization;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Tx.Cli.Templates.Models.Common;
using Chrysalis.Tx.Cli.Utils;
using Chrysalis.Cbor.Types.Plutus;
using Chrysalis.Tx.Cli.Templates.Parameters.NftPosition;

string words = "lock drive scheme smooth staff gym laptop identify client pigeon annual run below boat perfect resource april laundry upset potato sorry inhale planet hedgehog";

Mnemonic mnemonic = Mnemonic.Restore(words, English.Words);

PrivateKey accountKey = mnemonic
            .GetRootKey()
            .Derive(PurposeType.Shelley, DerivationType.HARD)
            .Derive(CoinType.Ada, DerivationType.HARD)
            .Derive(0, DerivationType.HARD);

PrivateKey privateKey = accountKey
            .Derive(RoleType.ExternalChain)
            .Derive(0);

PublicKey pkPub = privateKey.GetPublicKey();

PublicKey skPub = accountKey
            .Derive(RoleType.Staking)
            .Derive(0)
            .GetPublicKey();


var provider = new Blockfrost("previewajMhMPYerz9Pd3GsqjayLwP5mgnNnZCC");
string tanAddress = "addr_test1qzxhwhx5ayuhcg6e7xcufy0ta6z5z2q6hwjl8m2r9cmcst0n3lfh3d6a7ege7gepfnhz2gxnm2rsvyd7yngf878k47wqjrmaqk";
string validatorAddress = "addr_test1wrffnmkn0pds0tmka6lsce88l5c9mtd90jv2u2vkfguu3rg7k7a60";
string protocolParamsAddress = "addr_test1wpr6c5g3nj3p22cq3m0q35lyzvf0ccgrajl228yha8gtadc9enk92";

// Sample transfer Tx
// var transfer = TransactionTemplateBuilder<ulong>.Create(provider)
// .AddStaticParty("tan", tanAddress, true)
// .AddInput((options, amount) =>
// {
//     options.From = "rico";
// })
// .AddOutput((options, amount) =>
// {
//     options.To = "rico";
//     options.Amount = new Lovelace(amount);

// })
// .Build();

// Transaction transferUnSignedTx = await transfer(10000000UL);
// Transaction transferSignedTx = transferUnSignedTx.Sign(privateKey);
// string txHash = await provider.SubmitTransactionAsync(transferSignedTx);
// Console.WriteLine($"Transfer Tx Hash: {txHash}");

// Sample Unlock Tx 
// RedeemerDataBuilder<UnlockParameters, CborIndefList<Indices>> withdrawRedeemerBuilder = (mapping, parameters) =>
// {
//     List<PlutusData> actions = [];
//     var (inputIndex, outputIndices) = mapping.GetInput("borrow");

//     List<ulong> outputIndicesData = [];
//     foreach (var (_, outputIndex) in outputIndices)
//     {
//         outputIndicesData.Add(outputIndex);
//     }
    
//     Indices indices = new Indices(
//         inputIndex,
//         new OutputIndices(outputIndices["main"], outputIndices["fee"], outputIndices["change"])
//     );

//     return new CborIndefList<Indices>([indices]);
// };


// RedeemerDataBuilder<UnlockParameters, PlutusData> spendRedeemerBuilder = (mapping, parameters) =>
// {
//     return new PlutusConstr([])
//     {
//         ConstrIndex = 121
//     };
// };

// string scriptRefTxHash = "54ffbc45dd2518ca808f16e516e9521023a546625ebd3d9047c5f98f312b5c4e";
// string lockTxHash = "c20293900913bbd73e6d92bafb6cad789256885ed09dae8c0da8523196ea390e";
// string withdrawalAddress = "stake_test17rffnmkn0pds0tmka6lsce88l5c9mtd90jv2u2vkfguu3rg77q9d9";

// var unlockLovelace = TransactionTemplateBuilder<UnlockParameters>.Create(provider)
//     .AddStaticParty("rico", ricoAddress, true)
//     .AddStaticParty("validator", validatorAddress)
//     .AddStaticParty("withdrawal", withdrawalAddress)
//     .AddReferenceInput((options, unlockParams) =>
//     {
//         options.From = "validator";
//         options.UtxoRef = unlockParams.ScriptRefUtxoOutref;
//     })
//     .AddInput((options, unlockParams) =>
//     {
//         options.From = "validator";
//         options.UtxoRef = unlockParams.LockedUtxoOutRef;
//         options.Id = "borrow";
//         options.SetRedeemerBuilder(spendRedeemerBuilder);
//     })
//     .AddOutput((options, unlockParams) =>
//     {
//         options.To = "rico";
//         options.Amount = unlockParams.MainAmount;
//         options.Datum = unlockParams.MainDatum;
//         options.AssociatedInputId = "borrow";
//         options.Id = "main";
//     })
//     .AddOutput((options, unlockParams) =>
//     {
//         options.To = "rico";
//         options.Amount = unlockParams.FeeAmount;
//         options.Datum = unlockParams.FeeDatum;
//         options.AssociatedInputId = "borrow";
//         options.Id = "fee";
//     })
//     .AddOutput((options, unlockParams) =>
//     {
//         options.To = "rico";
//         options.Amount = unlockParams.ChangeAmount;
//         options.Datum = unlockParams.ChangeDatum;
//         options.AssociatedInputId = "borrow";
//         options.Id = "change";
//     })
//     .AddWithdrawal((options, unlockParams) =>
//     {
//         options.From = "withdrawal";
//         options.Amount = unlockParams.WithdrawalAmount;
//         options.Id = "withdrawal";
//         options.SetRedeemerFactory(withdrawRedeemerBuilder);
//     })
//     .Build();


// UnlockParameters unlockParams = new(
//     new TransactionInput(Convert.FromHexString(lockTxHash), 0),
//     new TransactionInput(Convert.FromHexString(scriptRefTxHash), 0),
//     null,
//     new Lovelace(20000000),
//     new Lovelace(10000000),
//     new Lovelace(5000000),
//     0,
//     new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString("446D61696E"))),
//     new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString("43666565"))),
//     new InlineDatumOption(1, new CborEncodedValue(Convert.FromHexString("466368616E6765"))),
//    null
// );

// Transaction unlockUnsignedTx = await unlockLovelace(unlockParams);
// Transaction unlockSignedTx = unlockUnsignedTx.Sign(privateKey);
// string unlockTxHash = await provider.SubmitTransactionAsync(unlockSignedTx);
// Console.WriteLine($"Unlock Tx Hash: {unlockTxHash}");

// string levvyParamsAddress = "addr_test1wrzw5nxvycsz2fwk5xkrgwec430pkvpe2saghxa3xj2zqsqx78h34";
// string borrowValidatorAddress = "addr_test1wq2gmfnfs3u9mrz52munwsrdmrznk79ua40kpagmv9gas7gtzfurs";
// string claimValidatorAddress = "addr_test1wqrwjf8w3av29d63rnxkvlgh7m5588dx5jq4ffptx269k5gym574e";
// string forecloseValidatorAddress = "addr_test1wz2lknk3cg99jeefr675x84uxun4hfjhdw0zfaak9tfkklgws844u";

// DeployScriptParameters deployParams = new(
//     forecloseValidatorAddress, 
//     ScriptType.PlutusV3, 
//     "5923a90101003229800aba4aba2aba1aba0aab9faab9eaab9dab9cab9a488888888a60022a660049212e657870656374206f75747075745f646174756d3a204c65767679446174756d203d206f75747075745f646174756d00168a998012493465787065637420696e7075745f646174756d3a2050726f746f636f6c506172616d73446174756d203d20696e7075745f6461746100168a998012492a65787065637420616374696f6e3a20416374696f6e203d2072656465656d65725f656e7472792e326e6400168a998012491f72656465656d65723a204c6973743c4f75747075745265666572656e63653e00168a998012490d72656465656d65723a20496e7400164888896600330013009375401f2300d300e300e0019b87480026e952000918069807000c88c8cc00400400c896600200314a115980098019808000c528c4cc008008c04400500a201c9b87480126e1d20069b87480226e1d20029180698071807180718071807180718071807000c8c034c038c038c038c038c038c038c0380064601a601c601c601c601c601c601c0032232330010010032233003001300200248888888888888a6002603601d301a00e9112cc00400a298103d87a80008acc004c03c0062601c66038603a00497ae08cc00400e603c005337000029000a006405c80da4444646600200200a4464b300130130018991919800800804112cc00400629422b30013371e6eb8c09000400e2946266004004604a00280f10221bae3021301e37540051598009806000c4c8cc004004dd61811180f9baa0032259800800c528c5660026600a00a604600313300200230240018a504074810a2b3001300f0018998081bac3021301e37540044660080080031598009807000c4c8cdc49bad3022001323300100137586046604800444b30010018a400113322598009980400400144cdc0000a400510014080604800266004004604a0028110c078dd50014566002601a00313322598009807180f9baa0018992cc004c03cc080dd5180a18109baa30143021375401113371200200713371000200680f0dd6981198101baa0018a5040746eb4c084c078dd50011810980f1baa3011301e375400b15980099b874802800626644b3001300e301f375400313259800980798101baa301430213754604860426ea8022266e2400c006266e2000c00501e1bad30233020375400314a080e8dd69810980f1baa0023021301e37546042603c6ea8016264646600200200c44b30010018a508acc004cdd780198101812000c528c4cc008008c09400501e20443374a9001198101810980f1baa0024bd702036406c80d901b2036406c60386ea800644646600200200644b30010018a60103d87a8000899192cc004cdc8802800c56600266e3c014006260226603e603a00497ae08a60103d87a80004069133004004302100340686eb8c06c004c07800501c4888ca6002003004801a00222232598009809800c4c9660020030068992cc00400600f007803c01e264b30013025003802c0210221bae001409460440028100c078dd5001c566002601800313259800800c01a264b3001001803c01e264b30013025003899805800912cc00400a00f13259800800c66002015001898011814001a014805c02e01700b40a4604c00481220108110dd6000c01e00e8128c088005020180f1baa0038acc004c03c006264b300100180344c966002003007803c4c966002604a00713300b0012259800801401e264b30010018cc00402a003130023028003402900b805c02e0168148c09800902440210221bac001803c01d0251811000a040301e37540071598009807000c4c9660020030068992cc00400600f007803c4cc89660020030098992cc00400601500a8992cc004c0a000e26601c00244b300100280544c96600200319800806c006260046056006806a01d00e807403902c1814801204e805a04a375800300a80520503025001408c6eb4004c09000a00e8128c088005020180f1baa0038acc004c034006264b300100180344c966002003007803c01e264b30013025003802c0210221bad001803a04a30220014080603c6ea800e2b30013370e9005000c4c9660020030068992cc00400600f007803c4c966002604a0070058042044375a003007409460440028100c078dd5001c56600266e1d200c0018992cc00400600d13259800800c01e00f007803c4c966002604a0070058042044375c0028128c088005020180f1baa003802a036406c80d901b2036406c80d8c070dd5001488966002601e60326ea800e264b300100180144c966002003003801c00e0071332259800800c016264b3001001803401a00d0068992cc004c09000e01100740846eb80050241810800a03e375c00260400048108c07800501c180d1baa003800a02e4888888a6002604200f302130220079802802c888c966002602060426ea80062900044dd6981298111baa001407c64b300130103021375400314c0103d87a8000899198008009bab30263023375400444b30010018a6103d87a8000899192cc004cdc8803000c56600266e3c0180062603466050604c00497ae08a60103d87a8000408d133004004302a003408c6eb8c090004c09c005025203e3300600300248888c966002602800313259800803407603b1323300f001225980080144c8cc004004dd5981598161816181618161816181618161816181618141baa0102259800800c528c5660026464b30013019302a37540031332259800800c4cc8a6002444b300130273031375400713259800800c00a264b30010018992cc00400600913259800800c4c9660020030068992cc004006264b300100180444c96600200313259800800c02a264b30010018acc004c10000a2b30013031303b375401313259800800c032264b3001001806c03601b1332259800800c03e264b30010018acc004c11400a330010018cc0040323300100a8acc004c0d8c100dd500444c9660020030118992cc00400602501280944cc89660020030148992cc004006264b300100180b44c96600200313259800800c062264b30010018acc004c13800a330010058cc00400e330010018cc00403e0350194075019407101940710194071019412d01980cc0660328278c13000504a1826001405e02f01780ba09a304a0014120609400501580ac05602a8258c1200050461bad001304700280920903045001410c60826ea802202081f2020809a020809a020809a020821202101080840410461821800a082375a002608400500d410c608000281f0c0f0dd5004c02d039402d03d402e01700b805a082303e00140f0607c005009804c02601281f8c0f000503a181e001401e00f007803a07a303a00140e06074005005802c01600a81d8c0e0005036181c001400e007003801a072303600140d060646ea800e002817a2b3001301d302e37540071323259800981318181baa0018992cc004cdd7981a98191baa0010078acc004cc0900388cdd7800981b18199baa0028cc00488966002605460686ea800e264b300100180144c966002003003801c00e0071332259800800c016264b3001001803401a00d006899912cc00400601113259800800c0260130098992cc004c10800e01700a40fc6eb40060128210c0fc00503d1bae001303e00240fc607800281d0dd7000981d8012078303900140dc606a6ea800e002819244464b3001302b0018992cc00400600713259800800c566002607800519800800c01600880e200881ca009004802401103d181d000a070303637540091598009812000c4c9660020030038992cc0040062b3001303c0028cc00400600b004406d00440e5004802401200881e8c0e8005038181b1baa004801206640cc60686ea800e44b30010018a4001133700900119801001181c000a06a9181b181b981b981b800c8c0d8c0dcc0dcc0dcc0dcc0dc00660646ea800d22222233223298009192cc004c0b8c0f0dd5000c4c9660020031980091000c0062b3001302e303d375400313034303e37546082607c6ea8006200281da074802207503a81d40e90431820181e9baa0018a9981da4812f65787065637420496e6c696e65446174756d286f75747075745f646174756d29203d206f75747075742e646174756d001640e8606460786ea8006607c0093301b3758606260766ea808cdd6981f181d9baa303e0024889660026066607a6ea800626644b30013035303f37540031323259800981818209baa001899912cc004c0d4c10cdd5000c4cc8966002b30013370eb3001303b304537546092609401b13259800acc004cdc7801a4500899b8f002489008a50411119800800d22100a44100409919800800c00e00481310441bab3039304637540111330120123259800981a98231baa0018a5eb7bdb18226eacc128c11cdd5000a0883302b37566072608c6ea80200090431bad303c304637546022608c6ea801229462a660889212069735f636f6c6c61746572616c5f73756666696369656e74203f2046616c73650014a0821a2b30015980099b8f375c6020608c6ea8c124c118dd50019b9437666092608c6ea801a29462a6608892011c69735f646174756d5f7461675f636f7272656374203f2046616c73650014a0821a2b30015980099baf3049304637546092608c6ea8c0e4c118dd5003182498231baa30493046375401114a3153304449012169735f73656e745f746f5f7363726970745f61646472657373203f2046616c73650014a0821a2b300159800992cc004c0d4c118dd5000c4cdc41bad30113047375400a6eb4c128c11cdd5000c52820883049304637546092608c6ea8c0c8c118dd50174528c54cc11124011569735f6c6f616e5f656e646564203f2046616c73650014a0821a2b30013259800981e18231baa0018acc00528c528c5660033001304a3047375400337586068608e6ea80be6066608e6ea80be6eacc0c8c11cdd5017a05a8a518a99822a481586d756c74697369672e736174697366696564287369672c2074782e65787472615f7369676e61746f726965732c2074782e76616c69646974795f72616e67652c2074782e7769746864726177616c7329203f2046616c73650014a0822104444c966002607a608e6ea80062b30013298009bae304c0019bae304c304d0019bab304c304d304d304d304d304937540633259800981f98249baa0018982698251baa0018a998242492065787065637420536f6d6528696e70757429203d206d617962655f696e7075740016411c660526eb0c130c124dd50189bad304c3049375400491112cc00566002b30014a314a31303b98009bab3040304d37546080609a6ea800600900340b0825229462a6609692012269735f61737365745f70726573656e745f696e5f696e70757473203f2046616c73650014a082522b300159800acc00566002b30014a314a313370f3001002802400d02c24002825229462a660969211c69735f757365725f746f6b656e5f6275726e6564203f2046616c73650014a082522b30013370f30010028024cdc5244104000643b0009800a4011337006e3400d2007801ae30816120018a518a99825a492169735f7265666572656e63655f746f6b656e5f6275726e6564203f2046616c73650014a08252294104a4528c54cc12d2413f6275726e5f746f6b656e28706f6c6963795f69642c2061737365745f6e616d652c206d696e742c2069735f666f7265636c6f7375726529203f2046616c73650014a0825229462a660969211e69735f746f6b656e5f6275726e696e675f76616c6964203f2046616c73650014a08252294104a0c120dd5182598241baa0028a518a998232493f7665726966795f6d696e7473286e66745f696e7075742c2061737365742c2074782e6d696e742c2069735f666f7265636c6f7375726529203f2046616c73650014a0822a2a6608c9214665787065637420536f6d65286e66745f696e7075745f696e64657829203d20696e7075745f696e64696365732e7265666572656e63655f6e66745f696e7075745f696e646578001641146074608e6ea802d044181c98231baa0048a518a9982224811d69735f6e66745f706f736974696f6e5f76616c6964203f2046616c73650014a0821a2941043452820868a50410d14a08218dd7182398221baa300f304437540046eb8c0dcc110dd5180798221baa0028a99821248144657870656374205265706179446174756d2872657061795f64657461696c7329203d206765745f6c657676795f646174756d28666f7265636c6f73655f6f75747075742900164104608a60846ea8004c01c0122a660809214765787065637420426f72726f77446174756d28626f72726f775f64657461696c7329203d206765745f6c657676795f646174756d2873656c665f696e7075742e6f757470757429001640fc600c606860826ea8004c10cc100dd5000c54cc0f92412465787065637420536f6d652873656c665f696e70757429203d2073656c665f696e707574001640f46082607c6ea8004cc078dd61820981f1baa026375a6082607c6ea800a2a6607892012b65787065637420536f6d6528666f7265636c6f73655f6f757470757429203d2073656c665f6f7574707574001640ec30020022233001222598009819181e1baa0038992cc00400600513259800800c00e26644b3001001802c4c966002003006803401a26644b300100180444c966002003159800982480144cc0bc018896600200519800807c8800600700c805a01e899192cc00400601b00d806c03626644b3001001807c03e01f00f899180318280039bae00141406eb8004c12400904e182380098250012090804a08c804c0260130094128608e0028228dd6800982300140190471822000a08437560026086005003801c00d0441820800a07e303d375400700140e844464b300130330018992cc00400600713259800800c5660026088005159800981a981f9baa0018992cc00400600b13259800800c4c9660020030078992cc004006264b3001001804c4c96600200313259800800c02e264b30010018992cc00400601b13259800800c03a01d00e899912cc00400602113259800800c56600260a2005198008064660020151980080446600200d19800800c04a022810202280da02280da02280da02280d20228272023011808c0450521827800a09a375a002609c00500e413c60980028250c13000a01900c806403104d1825000a090304a002805402a01500a412c60900028230c12000a01100880440210491823000a0883046002803401a00d006411c60880028210c100dd5000c01103d40110414012009004802208a30420014100607c6ea80122b3001302c0018992cc00400600713259800800c5660026088005159800981a981f9baa0018992cc00400600b13259800800c4c9660020030078992cc004006264b3001001804c4c96600200313259800800c02e264b30010018992cc00400601b13259800800c4c96600200300f8992cc00400602101080844cc89660020030128992cc004006264b300100180a44c96600200301580ac05602b13259800982b001c6600202319800807c6600201b19800805c6600201319800802405e02c812a02c810202c810202c810202c80fa02c80fa02c8298dd7000a0ac3053001414460a6005013809c04e02682a0c14400504f1bad001305000280820a2304e0014130609c00500e807403a01c8278c13000504a1826001403201900c806209a304a0014120609400500a805402a0148258c12000504618240014022011008804209230460014110608c005006803401a00c8238c11000504218201baa001802207a80220828024012009004411460840028200c0f8dd50024566002605e00313259800800c00e264b30010018acc004c11000a2b30013035303f375400313259800800c016264b30010018992cc00400600f13259800800c4c9660020030098992cc004006264b3001001805c4c96600200313259800800c036264b30010018992cc00400601f13259800800c04202101080844c96600260a2007198008064660020151980080446600200d19800802404a02280da02280da02280da02280d202280d20228270dd7000a0a2304e0014130609c00500e807403a01c8278c13000504a1826001403201900c806209a304a0014120609400500a805402a0148258c12000504618240014022011008804209230460014110608c005006803401a00c8238c11000504218201baa001802207a80220828024012009004411460840028200c0f8dd50024566002605c00313259800800c00e264b30010018acc004c11000a3300100891001400600b0044021004410500480240120088228c108005040181f1baa004801207640ec81d903b181e1baa003303c303c303c001454cc0c12401f76578706563740a2020202020202020202020202020202020202020202072656465656d65720a2020202020202020202020202020202020202020202020207c3e206c6973742e616e79280a20202020202020202020202020202020202020202020202020202020666e286f757472656629207b0a2020202020202020202020202020202020202020202020202020202020206f7574726566203d3d2076616c6964617465645f696e7075742e6f75747075745f7265666572656e63650a202020202020202020202020202020202020202020202020202020207d2c0a202020202020202020202020202020202020202020202020202029001640bd15330304913b6578706563742076616c6964617465645f696e7075742e6f75747075745f7265666572656e6365203d3d206f75747075745f7265666572656e6365001640bc606860626ea80062a6605e92017065787065637420536f6d652876616c6964617465645f696e70757429203d0a2020202020202020202020202020202020202020202073656c662e696e707574730a2020202020202020202020202020202020202020202020207c3e206c6973742e617428696e7075745f696e64657829001640b8660206eb0c0ccc0c0dd500c1bad303330303754606660606ea8004c0c8c0bcdd5001c528a05898171baa003488966002604e00313259800800c0b6264b30010018acc004c0e000a3300100180240b900540b903540ba05d02e8172072303600140d060646ea801a2b300130200018992cc00400605b13259800800c566002607000519800800c01205c802a05c81aa05d02e81740b9039181b000a0683032375400d1598009811800c4c96600200302d8992cc0040062b300130380028cc00400600902e401502e40d502e81740ba05c81c8c0d800503418191baa0068acc004c088006264b3001001816c4c966002003159800981c001466002003004817200a817206a81740ba05d02e40e4606c00281a0c0c8dd50034566002604200313259800800c0b6264b30010018acc004c0e000a3300100180240b900540b903540ba05d02e8172072303600140d060646ea801a058817902f205e40bc817844464b300130260018acc004c0c4dd5002400e00481922b3001301f0018acc004c0c4dd5002400e0048192004817102e18179baa00322232598009813000c4c9660020030038992cc00400600900480244c9660026070007006802a06a375a00300440e0606a0028198c0c4dd50024566002603e00315980098189baa004801c009032400902e205c302f3754007027813c09e04e8190c0b8c0acdd500098158014528a0503029001302c0018998010011816800c528204c40a913259800800c566002603a604e6ea8006264b300100181144c966002003023811c08e0471332259800800c096264b3001001813409a04d132598009819001c4c020c0c802604e8178dd6800c0990321817800a05a375c002605c0048178c0b000502a18141baa001810a04a810c08604302140b460540048140dd6003407603a8148c098c08cdd500245660026026003132332259800804407a03d01e8994c00488966002603e60526ea800e264b300100180144c96600200313259800800c012264b30010018acc004c0c800a330010038992cc004c090006264b3001001803c4c966002003159800981a80144c966002604e00313259800800c02a264b30010018acc004c0e000a33001001806402d00e402d035402e01700b805a072303600140d060646ea800a2b300130200018992cc00400601513259800800c02e01700b899912cc00400601b13259800800c03a01d00e899912cc00400602113259800800c0460230118992cc004c0fc00e02701240f06eb400602281f8c0f000503a1bad001303b0028072078303900140dc6eb4004c0e000a01681c8c0d800503418191baa002804a05e40bc60606ea8006010819201100880440210361819800a062302f3754005159800980e800c566002605e6ea800a00f00640c100640b08160c0b4dd5000c015008401502f401600b005802a066303000140b86060005003801c00e0068188c0b800502c18151baa003800a04e998039bac301a3027375401e6eb4026444b3001301f3029375400713259800800c00a264b3001001801c00e0071332259800800c016264b3001001803401a00d13259800981a001c02200e8188dd6800c0190341818800a05e375a002606000500340c4605c0028160c0a8dd5001c0050272444b3001301f302937540051323259800981098159baa0018acc0056600266e21200098009bab301f302c3754603e60586ea800a6eb8c0bcc0b0dd5019cdd7180f98161baa033402d14a3153302a4913469735f676c6f62616c5f70726f746f636f6c5f706172616d735f6964656e7469666965725f70726573656e74203f2046616c73650014a0814a2b30019800981118161baa302f302c37540033758603260586ea8052603060586ea80526eacc05cc0b0dd500a20248a518a9981524811c69735f63657274696669636174655f76616c6964203f2046616c73650014a0814a2941029454cc0a92417a65787065637420476c6f62616c506172616d7328676c6f62616c5f70726f746f636f6c5f706172616d735f646174756d29203d0a202020206765745f70726f746f636f6c5f706172616d735f646174756d28676c6f62616c5f70726f746f636f6c5f706172616d735f7265665f696e7075742e6f757470757429001640a464b3001301d302b375400313259800800c4cc8966002604800313259800800c0ae264b30010018acc004c0d400a2b300130263030375400313259800800c0b6264b30010018992cc00400605f13259800800c4c9660020030318992cc004006264b3001001819c4c96600200303481a40d20691332259800800c0da264b300100181bc0de06f037899912cc00400607313259800800c0ea07503a81d44cc896600200303c8992cc00400607b03d81ec0f6264b300130470038cc0040463300100f8cc00403602d03e409d03e407103e406903e41106eb80050471822000a084375c00260860048220c10400503f1bae00130400024104607c00281e0dd7000981e801207c303b00140e4607600503281940ca06481e0c0e4005037181c80140c20610308182074303700140d4606e00502e81740ba05c81c0c0d400503318189baa001816205c816206481640b205902c40d860660028188c0bcdd5001c566002603a00313259800800c0ae264b30010018acc004c0d400a2b300130263030375400313259800800c0b6264b30010018992cc00400605f13259800800c4c9660020030318992cc0040062b3001303b0028cc004016330010038acc004c0b0c0d8dd5000c4c9660020030338992cc004006264b300100181ac4c966002003159800981f80146600200719800800c03a06c80f206c80f206c81e206d03681b40d9040181e800a076303d00281a40d206903440f8607600281c8c0dcdd5000c0c903440c901040c900e40c903840ca0650328192078303900140dc607200503081840c206081d0c0dc005035181b80140ba05d02e8172070303500140cc60626ea80060588172058819205902c81640b10361819800a062302f375400702a40b081604004c0b0dd5000c0a20510288142064302f302c3754003153302a4912d65787065637420496e6c696e65446174756d28696e7075745f6461746129203d206f75747075742e646174756d001640a4604260566ea8c078c0acdd5000981698151baa0028a9981424815465787065637420536f6d6528676c6f62616c5f70726f746f636f6c5f706172616d735f7265665f696e70757429203d0a20202020676c6f62616c5f70726f746f636f6c5f706172616d735f7265665f696e7075740016409c2223259800980f800c4c9660020030038992cc0040060090048024012264b30013031003803401502e1bae00140c4605c0028160c0a8dd50024566002603000313259800800c00e264b300100180240120090048992cc004c0c400e00d00540b86eb80050311817000a058302a3754009002409c8138c0a0dd5001c07902b1bad302700130273028001302337540091640808100c084dd500188a4d153300749011856616c696461746f722072657475726e65642066616c7365001365640182612ad8799f581cdef68337867cb4f1f95b6b811fedbfcdd7780d10a95cc072077088ea47706172616d7331ff0001"
//     );

// CommonTemplates commonTemplates = new(provider, tanAddress);

// Transaction deployUnsignedTx = await commonTemplates.DeployScript(deployParams);
// Transaction deploySignedTx = deployUnsignedTx.Sign(privateKey);
// string deployTxHash = await provider.SubmitTransactionAsync(deploySignedTx);
// Console.WriteLine($"Deploy Tx Hash: {deployTxHash}");


LevvyTemplates levvyTemplates = new(provider, tanAddress);
LevvyNftTemplates levvyNftTemplates = new(provider, tanAddress);

// LockProtocolParamsParameters lockParams = new(
//     levvyParamsAddress,

// )

// string policyId = "def68337867cb4f1f95b6b811fedbfcdd7780d10a95cc072077088ea";
// string assetName = "706172616d7331";
// string paramsValidatorAddress = "addr_test1wr00dqehse7tfu0etd4cz8ldhlxaw7qdzz54esrjqacg36sp45dt3";

// WalletAddress tanMainAddress = new(tanAddress);

// LevvyGlobalProtocolParams levvyGlobalProtocolParams = new(
//     new GlobalParamsDetails(
//         new Rational(5, 100), 
//         new Chrysalis.Cbor.Types.Plutus.Address.Address(new VerificationKey(tanMainAddress.GetPaymentKeyHash()!), new None<Inline<PlutusCredential>>()),
//         new Signature(tanMainAddress.GetPaymentKeyHash()!),
//         Convert.FromHexString(policyId),
//         Convert.FromHexString("54657374"),
//         [],
//         []
//     )
//     );

// LockProtocolParamsParameters lockParams = new(paramsValidatorAddress, policyId, assetName, levvyGlobalProtocolParams, null);

// Transaction lockUnsignedTx = await levvyTemplates.LockPparams(lockParams);
// Transaction lockSignedTx = lockUnsignedTx.Sign(privateKey);
// string lockParamsTxHash = await provider.SubmitTransactionAsync(lockSignedTx);
// Console.WriteLine($"Lock Params Tx Hash: {lockParamsTxHash}");

// // Lend Sample Tx
// string mainValidatorAddress = "addr_test1wra8f56lfvx53trz3zk9e6n3728gqat945ycg53j7g5kvrc5qqfl0";

// var lender = new Multisig(new Signature(tanMainAddress.GetPaymentKeyHash()!));
// var principalDetails = new AssetDetails([], [], 5000000);
// var collateralDetails = new AssetDetails([], [], 5000000);
// var interestDetails = new AssetDetails([], [], 3000000);

// LendDetails lendDetails = new(lender, principalDetails, collateralDetails, interestDetails, new PosixTime(0), new Token());
// LendDatum lendDatum = new(lendDetails);
// LendParams lendParams = new(lendDatum, mainValidatorAddress);

// var multiSigLend = levvyTemplates.MultiSigLend();
// Transaction lockUnsignedTx = await multiSigLend(lendParams);
// Transaction lockSignedTx = lockUnsignedTx.Sign(privateKey);
// Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(lockSignedTx)));
// string lendTxHash = await provider.SubmitTransactionAsync(lockSignedTx);
// Console.WriteLine($"Lend Params Tx Hash: {lendTxHash}");

// var cancel = levvyTemplates.Cancel();
// TransactionInput cancelLockedUtxo1 = new(Convert.FromHexString("06de79b9e917b4f075eeb15a911620ee037c46fe297de1da4176d6eef551d928"), 0);

// CancelParams cancelParams = new([cancelLockedUtxo1], new Lovelace(10000000));

// Transaction cancelUnsignedTx = await cancel(cancelParams);
// Transaction cancelSignedTx = cancelUnsignedTx.Sign(privateKey);
// Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(cancelSignedTx)));
// string cancelTxHash = await provider.SubmitTransactionAsync(cancelSignedTx);
// Console.WriteLine($"Cancel Tx Hash: {cancelTxHash}");

// // Borrow Sample Tx
// LevvyIdentifier lender = new Multisig(new Signature(tanMainAddress.GetPaymentKeyHash()!));
// LevvyIdentifier borrower = new Multisig(new Signature(tanMainAddress.GetPaymentKeyHash()!));

// var principalDetails = new AssetDetails([], [], 5000000);
// var collateralDetails = new AssetDetails([], [], 5000000);
// var interestDetails = new AssetDetails([], [], 3000000);

// BorrowDetails borrowDetails = new(
//     lender,
//     borrower,
//     principalDetails,
//     collateralDetails,
//     interestDetails,
//     new PosixTime(0),
//     new Token(),
//     Convert.FromHexString("3AD4A2909AFA3B3A4C2DB072F9C9C5C9001027F25D971C9E11CA01CE9D3075DE")
// );
// BorrowDatum borrowDatum = new(borrowDetails);

// TransactionInput borrowLockedUtxo1 = new(Convert.FromHexString("2902357749fb52f0bb16de59e810bb1fc28c81ff26d965a38b76d19dbaaf0d89"), 0);

// BorrowParams borrowParams = new([borrowLockedUtxo1], new Lovelace(5000000), new Lovelace(5000000), borrowDatum);

// var borrow = levvyTemplates.Borrow();
// Transaction unsignedTx = await borrow(borrowParams);
// Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(unsignedTx)));
// Transaction signedTx = unsignedTx.Sign(privateKey);
// string borrowTxHash = await provider.SubmitTransactionAsync(signedTx);
// Console.WriteLine($"Transaction Id: {borrowTxHash}");

// // Repay Sample Tx
// LevvyIdentifier lender = new Multisig(new Signature(tanMainAddress.GetPaymentKeyHash()!));
// LevvyIdentifier borrower = new Multisig(new Signature(tanMainAddress.GetPaymentKeyHash()!));

// var principalDetails = new AssetDetails([], [], 5000000);
// var collateralDetails = new AssetDetails([], [], 5000000);
// var interestDetails = new AssetDetails([], [], 3000000);

// RepayDetails repayDetails = new(
//     lender,
//     borrower,
//     collateralDetails,
//     principalDetails,
//     interestDetails,
//     Convert.FromHexString("8AB79DB52EBBA38FE9B4EDFBCD0F3A837AC25A35871961DB074D9C8010D4C4F7")
// );
// RepayDatum repayDatum = new(repayDetails);

// TransactionInput repayLockedUtxo1 = new(Convert.FromHexString("25e615156cd9e10be59d9a17776ef7958d46d1f57919a5fc58a616902f0e185c"), 0);

// RepayParams repayParams = new([repayLockedUtxo1], repayDatum);

// var repay = levvyTemplates.Repay();
// Transaction unsignedRepayTx = await repay(repayParams);
// Transaction signedRepayTx = unsignedRepayTx.Sign(privateKey);
// Console.WriteLine(signedRepayTx.ToCborHex());
// string txId = await provider.SubmitTransactionAsync(signedRepayTx);
// Console.WriteLine($"Transaction Id: {txId}");

// // Claim Sample Tx
// LevvyIdentifier lender = new Multisig(new Signature(tanMainAddress.GetPaymentKeyHash()!));

// TransactionInput claimLockedUtxo1 = new(Convert.FromHexString("1117aa1b22088baf1987aa01c8ac661eb872be0e1ee7eb164526aeb5503d97cf"), 0);

// ClaimParams claimParams = new([claimLockedUtxo1]);

// var claim = levvyTemplates.Claim();
// Transaction unsignedClaimTx = await claim(claimParams);
// Console.WriteLine(unsignedClaimTx.ToCborHex());
// Transaction signedClaimTx = unsignedClaimTx.Sign(privateKey);
// string txId = await provider.SubmitTransactionAsync(signedClaimTx);
// Console.WriteLine($"Transaction Id: {txId}");


// Foreclose Sample Tx
// LevvyIdentifier lender = new Multisig(new Signature(tanMainAddress.GetPaymentKeyHash()!));
// LevvyIdentifier borrower = new Multisig(new Signature(tanMainAddress.GetPaymentKeyHash()!));

// var principalDetails = new AssetDetails([], [], 5000000);
// var collateralDetails = new AssetDetails([], [], 5000000);
// var interestDetails = new AssetDetails([], [], 3000000);

// RepayDetails repayDetails = new(
//     lender,
//     borrower,
//     collateralDetails,
//     principalDetails,
//     interestDetails,
//     Convert.FromHexString("4E84F70A72D75F9462FEC04A842CF7399F430BD46FB1EA419732C2130B449609")
// );
// RepayDatum repayDatum = new(repayDetails);

// TransactionInput lockedUtxo1 = new(
//     Convert.FromHexString("009c44e4c96920a78e30ba0b81112fc5705880b59b27cf37725bcbc3edb5aec3"),
//     0
// );

// ForecloseParams forecloseParams = new([lockedUtxo1], repayDatum);

// var foreclose = levvyTemplates.Foreclose();
// Transaction unsignedForecloseTx = await foreclose(forecloseParams);
// Console.WriteLine(unsignedForecloseTx.ToCborHex());
// Transaction signedForecloseTx = unsignedForecloseTx.Sign(privateKey);
// string txId = await provider.SubmitTransactionAsync(signedForecloseTx);
// Console.WriteLine($"Transaction Id: {txId}");


// -------------------------------------------------//
// Lend Nft Position Sample Tx
// string mainValidatorAddress = "addr_test1wra8f56lfvx53trz3zk9e6n3728gqat945ycg53j7g5kvrc5qqfl0";
// var policyId = "fa74d35f4b0d48ac6288ac5cea71f28e807565ad09845232f229660f";
// var userAssetName = "000DE1400067A2D9BD20033FB935C2839FE1C7F6BEFACCDAB98F3D645B8FE262".ToLowerInvariant();
// var referenceAssetName = "000643B00067A2D9BD20033FB935C2839FE1C7F6BEFACCDAB98F3D645B8FE262".ToLowerInvariant();

// var principalDetails = new AssetDetails([], [], 5_000_000);
// var collateralDetails = new AssetDetails([], [], 5_000_000);
// var interestDetails = new AssetDetails([], [], 3_000_000);

// LevvyIdentifier lender = new NftPosition(new Subject(
//     Convert.FromHexString(policyId),
//     Convert.FromHexString(userAssetName)
// ));

// LendDetails lendDetails = new(lender, principalDetails, collateralDetails, interestDetails, new PosixTime(0), new Token());

// Dictionary<PlutusData, PlutusData> metadata = new()
// {
//     {
//         new PlutusBoundedBytes(Convert.FromHexString("6e616d65")),
//         new PlutusBoundedBytes(Convert.FromHexString("54657374") )
//     },
//     {
//         new PlutusBoundedBytes(Convert.FromHexString("696d616765")),
//         new PlutusBoundedBytes(Convert.FromHexString(""))
//     }
// };

// Cip68<LevvyDatum> cip68Metadata = new(new PlutusMap(metadata), 1, new LendDatum(lendDetails));
// NftPositionDatum nftPositionDatum = new(cip68Metadata);
// LendMintParams lendMintParams = new(
//     NftPositionDatum: nftPositionDatum,
//     PrincipalDetails: principalDetails,
//     ValidatorAddress: mainValidatorAddress,
//     MintPolicy: policyId,
//     UserAssetName: userAssetName,
//     ReferenceAssetName: referenceAssetName
// );

// var lendNftPosition = levvyNftTemplates.Lend();
// Transaction lockUnsignedTx = await lendNftPosition(lendMintParams);
// Transaction lockSignedTx = lockUnsignedTx.Sign(privateKey);
// Console.WriteLine(lockSignedTx.ToCborHex());
// string lendTxHash = await provider.SubmitTransactionAsync(lockSignedTx);
// Console.WriteLine($"Sample Lend Tx Hash: {lendTxHash}");

// -------------------------------------------------//
// // Cancel Lend Nft Position Sample Tx
// var cancel = levvyNftTemplates.Cancel();
// TransactionInput cancelLockedUtxo1 = new(Convert.FromHexString("839857a9581d171210cf369eed0ed8d81fc7e2007658324ba76eb054fe219afc"), 0);

// string mintPolicy = "fa74d35f4b0d48ac6288ac5cea71f28e807565ad09845232f229660f";
// string referenceAssetName = "000643b0008b79d23db47ceea50f8ca851b9c94f26cedb1ba33b0a030199e8b1";
// string userAssetName = "000de140008b79d23db47ceea50f8ca851b9c94f26cedb1ba33b0a030199e8b1";

// Value principalAmount = new Lovelace(2_000_000);

// CancelMintParams cancelMintParams = new(
//     [cancelLockedUtxo1],
//     principalAmount,
//     mintPolicy,
//     referenceAssetName,
//     userAssetName
// );

// Transaction cancelUnsignedTx = await cancel(cancelMintParams);
// Transaction cancelSignedTx = cancelUnsignedTx.Sign(privateKey);
// Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(cancelSignedTx)));
// string cancelTxHash = await provider.SubmitTransactionAsync(cancelSignedTx);
// Console.WriteLine($"Cancel Tx Hash: {cancelTxHash}");
// -------------------------------------------------//
// Borrow Lend Nft Position Sample Tx
var policyId = "fa74d35f4b0d48ac6288ac5cea71f28e807565ad09845232f229660f";
var lockedUserAssetName = "000DE1400067A2D9BD20033FB935C2839FE1C7F6BEFACCDAB98F3D645B8FE262".ToLowerInvariant();
var lockedReferenceAssetName = "000643B00067A2D9BD20033FB935C2839FE1C7F6BEFACCDAB98F3D645B8FE262".ToLowerInvariant();

var userAssetName = "000DE140002DA58A063A84F5D6CE1B9B5148EBBCCF54EFCC1B4FE157FBABFFB6".ToLowerInvariant();
var referenceAssetName = "000643B0002DA58A063A84F5D6CE1B9B5148EBBCCF54EFCC1B4FE157FBABFFB6".ToLowerInvariant();

LevvyIdentifier lender = new NftPosition(new Subject(
    Convert.FromHexString(policyId),
    Convert.FromHexString(lockedUserAssetName)
));
LevvyIdentifier borrower = new NftPosition(new Subject(
    Convert.FromHexString(policyId),
    Convert.FromHexString(userAssetName)
));

var principalDetails = new AssetDetails([], [], 5000000);
var collateralDetails = new AssetDetails([], [], 5000000);
var interestDetails = new AssetDetails([], [], 3000000);

BorrowDetails borrowDetails = new(
    lender,
    borrower,
    principalDetails,
    collateralDetails,
    interestDetails,
    new PosixTime(0),
    new Token(),
    Convert.FromHexString("2DA58A063A84F5D6CE1B9B5148EBBCCF54EFCC1B4FE157FBABFFB6F59A5ECFF6")
);

Dictionary<PlutusData, PlutusData> metadata = new()
{
    {
        new PlutusBoundedBytes(Convert.FromHexString("6e616d65")),
        new PlutusBoundedBytes(Convert.FromHexString("54657374") )
    },
    {
        new PlutusBoundedBytes(Convert.FromHexString("696d616765")),
        new PlutusBoundedBytes(Convert.FromHexString(""))
    }
};

Cip68<LevvyDatum> cip68Metadata = new(new PlutusMap(metadata), 1, new BorrowDatum(borrowDetails));
NftPositionDatum nftPositionDatum = new(cip68Metadata);

TransactionInput borrowLockedUtxo1 = new(Convert.FromHexString("591b38da8316a9f2f331f7ef37c510f8baedf09b37a03b1ccad751897d1e6199"), 0);

BorrowMintParams borrowMintParams = new(
    LockedUtxos: [borrowLockedUtxo1],
    NftPositionDatum: nftPositionDatum,
    PrincipalDetails: principalDetails,
    CollateralDetails: collateralDetails,
    LockedReferenceAssetName: lockedReferenceAssetName,
    MintPolicy: policyId,
    ReferenceAssetName: referenceAssetName,
    UserAssetName: userAssetName
);

var borrow = levvyNftTemplates.Borrow();
Transaction unsignedTx = await borrow(borrowMintParams);
Transaction signedTx = unsignedTx.Sign(privateKey);
Console.WriteLine(signedTx.ToCborHex());
string borrowTxHash = await provider.SubmitTransactionAsync(signedTx);
Console.WriteLine($"Transaction Id: {borrowTxHash}");