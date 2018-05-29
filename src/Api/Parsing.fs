module Api.Parsing

open Api
open FSharp.Data
open Api.Types
open Consensus
open Consensus.Types
open FsBech32
open System.Text
open Newtonsoft.Json

let private getSpend asset amount =
    match Asset.fromString asset with
    | Some asset -> Ok { asset = asset; amount = uint64 amount }
    | None -> Error "invalid asset"

let parseSendJson chain json =
    try
        let json = SendRequestJson.Parse json

        if String.length json.Password = 0 then
            Error "Password is empty"
        else
            Address.decodePK chain json.Address
            |> function
            | Error err ->
                Error ("Address is invalid: " + err)
            | Ok pkHash ->
                getSpend json.Asset json.Amount
                |> Result.map (fun spend -> (pkHash, spend, json.Password))
    with _ as ex ->
        Error ("Json invalid: " + ex.Message)

let parseContractExecuteJson chain json =
    try
        let json = ContractExecuteRequestJson.Parse json

        if String.length json.Password = 0 then
            Error "Password is empty"
        else
            match Address.decodeContract chain json.Address with
            | Error err -> Error ("Address is invalid: " + err)
            | Ok contractId ->
                let mutable spends = Map.empty
                let mutable errors = List.empty

                for item in json.Spends do
                    getSpend item.Asset item.Amount
                    |> function
                    | Ok spend -> spends <- Map.add spend.asset spend.amount spends
                    | Error err -> errors <- err :: errors
                    |> ignore

                if List.isEmpty errors then
                    let data =
                        if System.String.IsNullOrEmpty json.Data then
                            None
                        else
                            match Base16.decode json.Data with
                            | Some data ->
                                match Serialization.Data.deserialize data with
                                | Some data -> Some data
                                | None -> failwith "Invalid Data"
                            | None -> failwith "Invalid Data"

                    let sign =
                        if System.String.IsNullOrEmpty json.Options.Sign then
                            None
                        else
                            Some json.Options.Sign

                    Ok (contractId, json.Command, data, json.Options.ReturnAddress, sign, spends, json.Password)
                else
                    errors
                    |> String.concat " "
                    |> Error
    with _ as ex ->
        Error ("Json is invalid: " + ex.Message)

let parseContractActivateJson json =
    try
        let json = ContractActivateRequestJson.Parse json

        if String.length json.Code = 0 then
            Error "Contract code is empty"
        else if String.length json.Password = 0 then
            Error "Password is empty"
        else if json.NumberOfBlocks = 0 then
            Error "Number of blocks is zero"
        else
            Ok (json.Code, uint32 json.NumberOfBlocks, json.Password)
    with _ as ex ->
        Error ("Json is invalid: " + ex.Message)

let parseContractExtendJson chain json =
    try
        let json = ContractExtendRequestJson.Parse json

        if String.length json.Password = 0 then
            Error "Password is empty"
        else if json.NumberOfBlocks = 0 then
            Error "Number of blocks is zero"
        else
            match Address.decodeContract chain json.Address with
            | Error err -> Error ("Address is invalid: " + err)
            | Ok contractId -> Ok (contractId, uint32 json.NumberOfBlocks, json.Password)
    with _ as ex ->
        Error ("Json is invalid: " + ex.Message)

let parsePublishBlockJson json =
    try
        let json = PublishBlockJson.Parse json

        match Block.fromHex json.Block with
        | Some block -> Ok block
        | None -> Error "invalid block"
    with _ as ex ->
        Error ("Json is invalid: " + ex.Message)

let parseImportSeedJson json =
    try
        let json = ImportSeedJson.Parse json

        let mutable words = List.empty

        for item in json.Words do
            words <- item :: words

        if String.length json.Password = 0 then
            Error "Password is empty"
        else
            Ok (List.rev words, json.Password)
    with _ as ex ->
        Error ("Json is invalid: " + ex.Message)

let parseGetPublicKeyJson json =
    try
        let json = GetPublicKeyJson.Parse json

        if String.length json.Path = 0 then
            Error "Path is empty"
        else if String.length json.Password = 0 then
            Error "Password is empty"
        else
            Ok (json.Path, json.Password)
    with _ as ex ->
        Error ("Json is invalid: " + ex.Message)

let parseSignJson json =
    try
        let json = SignJson.Parse json

        if String.length json.Path = 0 then
            Error "Path is empty"
        else if String.length json.Password = 0 then
            Error "Password is empty"
        else
            match Hash.fromString json.Message with
            | Ok message ->
                Ok (message, json.Path, json.Password)
            | _ -> Error "invalid message"
    with _ as ex ->
        Error ("Json is invalid: " + ex.Message)

let parseCheckPasswordJson json =
    try
        let json = CheckPasswordJson.Parse json

        if String.length json.Password = 0 then
            Error "Password is empty"
        else
            Ok json.Password
    with _ as ex ->
        Error ("Json is invalid: " + ex.Message)

let parseTransactionsRequestJson json =
    try
        let json = TransactionsRequestJson.Parse json

        if json.Skip < 0 || json.Take < 0 then
            Error "Invalid values"
        else
            Ok (json.Skip, json.Take)
    with _ as ex ->
        Error ("Json is invalid: " + ex.Message)