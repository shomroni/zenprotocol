module Api.Tests.Json

open Consensus
open Types
open Api.Types
open FSharp.Data
open Infrastructure

let publishBlockJson block = 
    (new PublishBlockJson.Root(
        Block.toHex block
    )).JsonValue

let importSeedJson password mnemonicPhrase =
    (new ImportSeedJson.Root(
        password, 
        mnemonicPhrase
    )).JsonValue

let contractActivateRequestJson code rlimit blocks password =
    (new ContractActivateRequestJson.Root(
        code, 
        rlimit,
        blocks,
        password
    )).JsonValue

let contractExecuteFromTransactionJson address command messageBody sender tx =
    (new ContractExecuteFromTransactionJson.Root(
        address, 
        command, 
        messageBody,
        new ContractExecuteFromTransactionJson.Options(sender:string),
        tx
    )).JsonValue

let contractExecuteRequestJson address command messageBody returnAddress sign password spends =
    (new ContractExecuteRequestJson.Root(
        address,
        command,
        messageBody,
        new ContractExecuteRequestJson.Options(returnAddress, sign),
        spends
        |> List.map (fun { asset = asset; amount = amount } -> asset.ToString(), int64 amount)
        |> List.map (fun (asset, amount) -> new ContractExecuteRequestJson.Spend(asset, string amount))
        |> List.toArray,
        password
    )).JsonValue

let parseBalanceJson =
    Array.map (fun (assertBalance:BalanceResponseJson.Root) ->
        assertBalance.Asset.ToString()
        |> Asset.fromString 
        |> Option.get,
        assertBalance.Balance
    )

let getContractHistoryJson contractId skip take =
    (new GetContractHistoryJson.Root(
        ContractId.toString contractId, 
        skip, 
        take
    )).JsonValue

let parseContractCommandHistoryResultJson =
    Array.map (fun (contractData:JsonValue) ->
        JsonExtensions.GetProperty (contractData, "command") |> JsonExtensions.AsString,
        JsonExtensions.GetProperty (contractData, "messageBody"),
        JsonExtensions.GetProperty (contractData, "txHash") |> JsonExtensions.AsString
    )