using Chrysalis.Cbor.Types;
using Chrysalis.Cbor.Types.Cardano.Core.Common;
using Chrysalis.Cbor.Types.Cardano.Core.Transaction;
using Chrysalis.Cbor.Types.Plutus.Address;
using Chrysalis.Tx.Builders;
using Chrysalis.Tx.Cli;
using Chrysalis.Tx.Cli.Templates;
using Chrysalis.Tx.Cli.Templates.Models;
using Chrysalis.Tx.Cli.Templates.Parameters;
using Chrysalis.Tx.Extensions;
using Chrysalis.Tx.Models;
using Chrysalis.Tx.Providers;
using Chrysalis.Wallet.Models.Addresses;
using Chrysalis.Wallet.Models.Enums;
using Chrysalis.Wallet.Models.Keys;
using Chrysalis.Wallet.Words;
using WalletAddress = Chrysalis.Wallet.Models.Addresses.Address;
using PlutusCredential = Chrysalis.Cbor.Types.Plutus.Address.Credential;
using Chrysalis.Cbor.Serialization;

string words = "";

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
string ricoAddress = "addr_test1qpw9cvvdq8mjncs9e90trvpdvg7azrncafv0wtgvz0uf9vhgjp8dc6v79uxw0detul8vnywlv5dzyt32ayjyadvhtjaqyl2gur";
string validatorAddress = "addr_test1wrffnmkn0pds0tmka6lsce88l5c9mtd90jv2u2vkfguu3rg7k7a60";

// Sample transfer Tx
var transfer = TransactionTemplateBuilder<ulong>.Create(provider)
.AddStaticParty("rico", ricoAddress, true)
.AddInput((options, amount) =>
{
    options.From = "rico";
})
.AddOutput((options, amount) =>
{
    options.To = "rico";
    options.Amount = new Lovelace(amount);

})
.Build();

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

string levvyParamsAddress = "addr_test1wrzw5nxvycsz2fwk5xkrgwec430pkvpe2saghxa3xj2zqsqx78h34";

DeployScriptParameters deployParams = new(
    levvyParamsAddress, 
    ScriptType.PlutusV3, 
    "590ad701010033333322222229800aba4aba2aba1aba0aab9faab9eaab9dab9cab9a9bae0079bae0069bae0059bae0049bae0039bae002488888888888888a60022a660109212565787065637420706f6c6963795f69643a20506f6c6963794964203d2072656465656d657200168a998042492465787065637420706172616d733a20416374696f6e506172616d73203d20616374696f6e00168a998042492465787065637420646174613a204c6973743c416374696f6e3e203d2072656465656d657200168a998042491072656465656d65723a20416374696f6e00168a99804249175f72656465656d65723a204d696e7452656465656d65720016488889660033001300f375402b370e900048c04cc050c050c050c050c050c050c050c050c05000644646600200200644660060026004005370e900124444530013018005980b802c888c9660026010602e6ea800626006603660306ea80062a6602c92015f65787065637420536f6d652872646d7229203d0a2020202072656465656d6572730a2020202020207c3e2070616972732e6765745f666972737428576974686472617728536372697074287374616b696e675f76616c696461746f722929290016405464646600200200644b30010018a60103d87a80008992cc004cdd7802180d000c4cdd2a40006603a603600297ae0899801801980f8012030301d001406c66e952004330193374a90011980c9ba90034bd7025eb8244464b300130080018992cc00400600713259800800c0120090048992cc004c07c00e00d00540706eb400600880f8c07000501a180c1baa0048acc004c0140062b3001301837540090038012032801202a4054602c6ea800d222232332259800980400144c8cc896600200913232980080345660026465300132598009809800c407e2b30013370e9002000c407a2b30013370e9004000c40762b30013010001880e4406d020204040808100c084dd5004496600200301780bc4c8cc0480048966002005132330010010042259800800c528456600264b30010018a99813a48159657870656374205b616374696f6e2c202e2e5d203d0a20202020202020202020202020202020616374696f6e0a2020202020202020202020202020202020207c3e206275696c74696e2e756e636f6e7374725f6669656c647300168992cc004006330010018992cc004c06cc0a8dd5000c4cdd7981718159baa302e302b375400201f153302949017265787065637420536f6d6528696e70757429203d0a2020202020202020202020202020202074782e696e707574730a2020202020202020202020202020202020207c3e206c6973742e617428706172616d732e696e7075745f696e64696365732e73656c665f696e7075745f696e64657829001640a0660126eb0c0b4c0a8dd500a9bad302d302a3754605a60546ea800603e806203f01f80fc07d02f1816000a05430283754605600314a3133002002302c0014094814a264b30010018cc00400626004605600701b402101b80dc06e0368160c0a40090271bac00180bc05d0284dd5980898111baa00d40306002002444b30010028a60103d87a80008acc004c04c006266e9520003302530260024bd70466002007302700299b80001480050032040409114a3153301e49013270726f636573735f7370656e64287574786f2c207370656e645f76616c696461746f722c2073656c6629203f2046616c73650014a080ea0268008888c966002602600313259800800c00e264b30010018acc004c0a400a33001001802c0110074011026401200900480220543027001409460466ea80122b300130100018992cc00400600713259800800c566002605200519800800c016008803a0088132009004802401102a1813800a04a3023375400915980099b8748010006264b3001001801c4c9660020031598009814801466002003005802200e802204c802401200900440a8604e0028128c08cdd5002456600266e1d20060018992cc00400600713259800800c566002605200519800800c016008803a0088132009004802401102a1813800a04a3023375400915980099b8748020006264b3001001801c4c9660020031598009814801466002003005802200e802204c802401200900440a8604e0028128c08cdd5002400902020404080810102018109baa00322259800980898101baa0038992cc00400600513259800800c4c9660020030048992cc004006264b300100180344c96600200313259800800c022264b30010018992cc00400601513259800800c566002605e005159800980d98151baa0098992cc00400601913259800800c03601b00d899912cc00400601f13259800800c566002606800519800800c660020191980080545660026040605e6ea8022264b3001001808c4c966002003012809404a26644b300100180a44c96600200313259800800c05a264b30010018992cc00400603113259800800c566002607a00519800802c6600200719800800c4c9660026054003159800981d1baa01080dc06903b4566002604e003159800981d1baa01080dc06903b4069037206e3038375401f01940890194089019408901940e901980cc06603281f0c0ec005039181d801405e02f01780ba078303900140dc607200501580ac05602a81d0c0dc0050351bad0013036002809206e303400140c860606ea8022020816a02080ca02080ca02080ca020818a02101080840410351819000a060375a002606200500d40c8605e0028168c0acdd5004c02d028402d02c402e01700b805a060302d00140ac605a005009804c0260128170c0ac0050291815801401e00f007803a0583029001409c6052005005802c01600a8150c09c0050251813801400e007003801a0503025001408c60426ea800e00280f2023011808c045023180f800980f9810000980d9baa0038acc004c02c00a264b30010028992cc004c034c070dd5001c4c9660020030108992cc004006023011808c04626644b3001001809c4c96600200313259800800c056264b30010018acc004c0a000a330010038992cc004c054006264b300100180c44c96600200301980cc4c96600260580071330160012259800801403a264b300100180ec07603b13230033030004375a00301d40c0605a004815a0348148dd6000c0660328160c0a400502718129baa0028acc004c0480062b30013025375400500980ba04c80ba044408860466ea800602c806a02c812a02d01680b40590291813000a048302600280a4052029014409c60480028110dd7000981180120483021001407c603a6ea800e01e80d0566003300101392cc004006027013809c04e266e3cdd700080120449bab300b301c375400e803229462a6603492013570726f636573735f6d696e7428706f6c6963795f69642c206d696e745f76616c696461746f722c2073656c6629203f2046616c73650014a080ca01d00e80740390211bae301e301b375400716406080c0c060dd5000980e002980d980e0021149a2a6601a9211856616c696461746f722072657475726e65642066616c7365001365640302611e581cb455c8f953116c7476a5bff5ca0fe381ef4dc2579a23f90985f9ae5f004c011e581ce6ab9dc779237e4409f7f765da0e8e773f2e30cc616ef032e0f5de11004c011e581cc85d7329ff0c0b94cffa49eb25a039377457aae1e2a1121ae8d1a24d004c011e581c724f3430c14058028d0fddfb5c6dc8fcbfd17e85c3fa754daeb4a0e7004c011e581cef9f6eb650d54a1b08134495d1d29c7ce42934fdf17457bbd6cad773004c011e581cf6de7ac02d931e883fc91e835313d424ef21a082ba2a0a456cddcf370001"
    );


// CommonTemplates commonTemplates = new(provider, ricoAddress);

// Transaction deployUnsignedTx = await commonTemplates.DeployScript(deployParams);
// Transaction deploySignedTx = deployUnsignedTx.Sign(privateKey);
// string deployTxHash = await provider.SubmitTransactionAsync(deploySignedTx);
// Console.WriteLine($"Deploy Tx Hash: {deployTxHash}");


LevvyTemplates levvyTemplates = new(provider, ricoAddress);

// LockProtocolParamsParameters lockParams = new(
//     levvyParamsAddress,

// )

string policyId = "def68337867cb4f1f95b6b811fedbfcdd7780d10a95cc072077088ea";
string assetName = "706172616d7331";
string paramsValidatorAddress = "addr_test1wr00dqehse7tfu0etd4cz8ldhlxaw7qdzz54esrjqacg36sp45dt3";

WalletAddress ricoMainAddress = new(ricoAddress);

LevvyGlobalProtocolParams levvyGlobalProtocolParams = new(
    new GlobalParamsDetails(
        new Rational(5, 100), 
        new Chrysalis.Cbor.Types.Plutus.Address.Address(new VerificationKey(pkPub.Key), new None<Inline<PlutusCredential>>()),
        new Signature(pkPub.Key),
        Convert.FromHexString(policyId)
        )
    );

LockProtocolParamsParameters lockParams = new(paramsValidatorAddress, policyId, assetName, levvyGlobalProtocolParams, null);

// Transaction lockUnsignedTx = await levvyTemplates.LockPparams(lockParams);
// Transaction lockSignedTx = lockUnsignedTx.Sign(privateKey);
// string lockParamsTxHash = await provider.SubmitTransactionAsync(lockSignedTx);
// Console.WriteLine($"Lock Params Tx Hash: {lockParamsTxHash}");

string mainValidatorAddress = "addr_test1wra8f56lfvx53trz3zk9e6n3728gqat945ycg53j7g5kvrc5qqfl0";
WalletAddress address = new(ricoAddress);

// var lender = new Multisig(new Signature(address.GetPaymentKeyHash()!));
// var principalDetails = new AssetDetails([], [], 70000000);
// var collateralDetails = new AssetDetails([], [], 10000000);
// var interestDetails = new AssetDetails([], [], 1000000);
// LendDetails lendDetails = new(lender, principalDetails,collateralDetails, interestDetails, new PosixTime(0), new Token());
// LendDatum lendDatum = new(lendDetails);
// var multiSigLend = levvyTemplates.MultiSigLend();
// LendParams lendParams = new(lendDatum, mainValidatorAddress);


// Transaction lockUnsignedTx = await multiSigLend(lendParams);
// Transaction lockSignedTx = lockUnsignedTx.Sign(privateKey);
// string lockParamsTxHash = await provider.SubmitTransactionAsync(lockSignedTx);
// Console.WriteLine($"Lock Params Tx Hash: {lockParamsTxHash}");


var cancel = levvyTemplates.Cancel();
TransactionInput cancelLockedUtxo1 = new(Convert.FromHexString("3e3e8e674f502cdca4f05854508e16f2b09c0a88400421bcf638a130d010a8dc"), 0);
TransactionInput cancelLockedUtxo2 = new(Convert.FromHexString("3e3e8e674f502cdca4f05854508e16f2b09c0a88400421bcf638a130d010a8dc"), 1);
TransactionInput cancelLockedUtxo3 = new(Convert.FromHexString("3e3e8e674f502cdca4f05854508e16f2b09c0a88400421bcf638a130d010a8dc"), 2);


CancelParams cancelParams = new([cancelLockedUtxo1, cancelLockedUtxo2, cancelLockedUtxo3], new Lovelace(70000000));

Transaction cancelUnsignedTx = await cancel(cancelParams);
Transaction cancelSignedTx = cancelUnsignedTx.Sign(privateKey);
Console.WriteLine(Convert.ToHexString(CborSerializer.Serialize(cancelSignedTx)));
string cancelTxHash = await provider.SubmitTransactionAsync(cancelSignedTx);
Console.WriteLine($"Cancel Tx Hash: {cancelTxHash}");   