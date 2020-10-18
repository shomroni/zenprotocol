module AddressDB.View

open Consensus
open Wallet.Types
open Wallet.Address
open Consensus.Types
open DataAccess
open Types
open Hash
open Infrastructure
open Messaging.Services.AddressDB
open Zen.Types.Data
open Messaging.Services.Wallet

let addOutput outputs outpoint output =
    Map.add outpoint output outputs

let addAddressOutpoints addressOutpoints address outpoints =
    let outpoints = 
        outpoints @
        match Map.tryFind address addressOutpoints with 
        | Some outpoints -> outpoints
        | None -> []
    Map.add address outpoints addressOutpoints

let addContractHistory contractData contractId data =
    let data = 
        data @
        match Map.tryFind contractId contractData with 
        | Some data -> data
        | None -> []
    Map.add contractId data contractData
    
let setContractData contractData witnessPoint data =
    Map.add witnessPoint data contractData
    
let setContractConfirmationStatus contractData witnessPoint data =
    Map.add witnessPoint data contractData

type T =
    {
        outpointOutputs: Map<Outpoint, DBOutput>
        addressOutpoints: Map<Address, List<Outpoint>>
        contractHistory: Map<ContractId, List<WitnessPoint>>
        contractData: Map<WitnessPoint, string * data option>
        contractConfirmations: Map<WitnessPoint, ConfirmationStatus>
    }
    with
        member this.getOutputs outputs = // add given (db) onto view (memory)
            Map.fold addOutput outputs this.outpointOutputs
        member this.getAddressOutpoints addressOutpoints = // add given (db) onto view (memory)
            Map.fold addAddressOutpoints addressOutpoints this.addressOutpoints

let empty = {
    outpointOutputs = Map.empty
    addressOutpoints = Map.empty
    contractHistory = Map.empty
    contractData = Map.empty
    contractConfirmations = Map.empty
}

let witnessPoint txHash witnesses cw =
    {
        txHash = txHash
        index =
            witnesses
            |> List.findIndex ((=) (ContractWitness cw))
            |> uint32
    }

module OutpointOutputs =
    let get view dataAccess session outpoints =
        outpoints
        |> List.map (fun outpoint -> 
            match Map.tryFind outpoint view.outpointOutputs with 
            | Some dbOutput -> dbOutput
            | None -> OutpointOutputs.get dataAccess session outpoint)

module AddressOutpoints =
    let private get' view addresses =
        addresses
        |> List.map (fun address -> Map.tryFind address view.addressOutpoints)
        |> List.map (function 
            | Some list -> list
            | None -> List.empty)
        |> List.concat
    let get view dataAcesss session addresses =
        AddressOutpoints.get dataAcesss session addresses @ get' view addresses

module ContractHistory =
    let private get' view contractId =
        Map.tryFind contractId view.contractHistory
        |> function 
            | Some list -> list
            | None -> List.empty
    let get view dataAccess session contractId =
        ContractHistory.get dataAccess session contractId @ get' view contractId
        
module ContractData =
    let get view dataAccess session witnessPoint =
        Map.tryFind witnessPoint view.contractData
        |> Option.defaultWith (fun _ ->
            ContractData.get dataAccess session witnessPoint)
        |> fun (command, data) -> (command, data, witnessPoint.txHash)

module ContractConfirmations =
    let get view dataAccess session witnessPoint =
        Map.tryFind witnessPoint view.contractConfirmations
        |> Option.defaultWith (fun _ ->
            ContractConfirmations.get dataAccess session witnessPoint)

let mapUnspentTxOutputs outputs txHash confirmationStatus =
    outputs
    |> List.mapi (fun index output -> uint32 index, output)
    |> List.choose (fun (index, output) ->
        match output.lock with
        | Coinbase (_,pkHash)
        | PK pkHash -> Some (Address.PK pkHash, index, output)
        | Contract contractId -> Some (Address.Contract contractId, index, output)
        | _ -> None)
    |> List.map (fun (address, index, output) ->
        {    
            address = address
            outpoint = { txHash = txHash; index = index }
            spend = output.spend
            lock = output.lock
            status = Unspent
            confirmationStatus = confirmationStatus
        })

let addMempoolTransaction dataAccess session txHash tx view =
    tx.inputs
    |> List.choose (function | Outpoint outpoint -> Some outpoint | _ -> None)
    |> List.fold (fun view outpoint ->
        view
        |> Option.bind (fun view ->
            Map.tryFind outpoint view.outpointOutputs 
            |> function
            | Some dbOutput -> Some dbOutput
            | None -> DataAccess.OutpointOutputs.tryGet dataAccess session outpoint
            |> Option.map (fun dbOutput -> { dbOutput with status = Spent (txHash, Unconfirmed) })
            |> Option.map (fun dbOutput -> { view with outpointOutputs = addOutput view.outpointOutputs outpoint dbOutput })
        )
    ) (Some view)
    |> Option.map (fun view ->
        mapUnspentTxOutputs tx.outputs txHash Unconfirmed 
        |> List.fold (fun view dbOutput ->  
        {
            outpointOutputs = addOutput view.outpointOutputs dbOutput.outpoint dbOutput
            addressOutpoints = addAddressOutpoints view.addressOutpoints dbOutput.address [ dbOutput.outpoint ]
            contractHistory = view.contractHistory
            contractData = view.contractData
            contractConfirmations = view.contractConfirmations
        }) view)
    |> Option.map (fun view ->
        tx.witnesses
        |> List.fold (fun view -> function 
            | ContractWitness cw ->
                let witnessPoint = witnessPoint txHash tx.witnesses cw
                    
                {
                    outpointOutputs = view.outpointOutputs
                    addressOutpoints = view.addressOutpoints
                    contractHistory = addContractHistory view.contractHistory cw.contractId [ witnessPoint ]
                    contractData = setContractData view.contractData witnessPoint (cw.command, cw.messageBody)
                    contractConfirmations = setContractConfirmationStatus view.contractConfirmations witnessPoint Unconfirmed
                }                
            | _ ->
                view
        ) view)
    |> function
    | Some view' -> view'
    | None -> view
        
let fromMempool dataAccess session =
    List.fold (fun view (txHash,tx) -> addMempoolTransaction dataAccess session txHash tx view) empty
    

let getOutputs dataAccess session view mode addresses : PointedOutput list =
    AddressOutpoints.get view dataAccess session addresses
    |> OutpointOutputs.get view dataAccess session
    |> List.choose (fun dbOutput -> 
        if mode <> UnspentOnly || dbOutput.status = Unspent then 
            Some (dbOutput.outpoint, { spend = dbOutput.spend; lock = dbOutput.lock })
        else
            None)

let getBalance dataAccess session view mode addresses =
    getOutputs dataAccess session view mode addresses 
    |> List.fold (fun balance (_,output) ->
        match Map.tryFind output.spend.asset balance with
        | Some amount -> Map.add output.spend.asset (amount + output.spend.amount) balance
        | None -> Map.add output.spend.asset output.spend.amount balance
    ) Map.empty

let getConfirmations blockNumber =
    function
    | Confirmed (blockNumber',_,blockIndex) ->
        blockNumber - blockNumber' + 1ul, blockIndex
    | Unconfirmed ->
        0ul,0

let getOutputsInfo blockNumber outputs = 

    let incoming = List.map (fun (output:DBOutput) ->
        let txHash = output.outpoint.txHash
        let confirmations,blockIndex = getConfirmations blockNumber output.confirmationStatus

        txHash, output.spend.asset, output.spend.amount |> bigint,confirmations,blockIndex,output.lock) outputs

    let outgoing = List.choose (fun (output:DBOutput) ->
        match output.status with
        | Unspent -> None
        | Spent (txHash,confirmationStatus) ->
            let confirmations,blockIndex = getConfirmations blockNumber confirmationStatus

            (txHash, output.spend.asset, output.spend.amount |> bigint |> (*) -1I,confirmations,blockIndex,output.lock)
            |> Some) outputs

    incoming @ outgoing
    |> List.fold (fun txs (txHash, asset, amount, confirmations, blockIndex, lock) ->
        match Map.tryFind (txHash, asset) txs with
        | None -> Map.add (txHash, asset) (amount, confirmations, blockIndex, lock) txs
        | Some (amount',_,_,lock) -> Map.add (txHash, asset) (amount + amount', confirmations, blockIndex, lock) txs) Map.empty
    |> Map.toSeq
    |> Seq.map (fun ((txHash, asset),(amount, confirmations, blockIndex, lock)) ->
        if amount >= 0I then
            (txHash,TransactionDirection.In, {asset=asset;amount = uint64 amount}, confirmations, blockIndex, lock)
        else
            (txHash,TransactionDirection.Out, {asset=asset;amount = amount * -1I |> uint64}, confirmations, blockIndex, lock))
    |> List.ofSeq

let getTransactionCount dataAccess session view blockNumber addresses =
    let outputs =
        AddressOutpoints.get view dataAccess session addresses
        |> OutpointOutputs.get view dataAccess session
    List.length (outputs |> getOutputsInfo blockNumber)

let getHistory dataAccess session view skip take addresses =
    let account = DataAccess.Tip.get dataAccess session
    
    AddressOutpoints.get view dataAccess session addresses
    |> OutpointOutputs.get view dataAccess session
    |> getOutputsInfo account.blockNumber
    |> List.sortWith Wallet.Account.txComparer
    |> Wallet.Account.paginate skip take
    |> List.map (fun (txHash,direction,spend,confirmations,_,lock) -> txHash,direction,spend,confirmations,lock)

let getContractHistory dataAccess session view skip take contractId =
    let account = DataAccess.Tip.get dataAccess session

    let comparer a1 a2 =
        let comparer (index1, block1) (index2, block2) = 
            if index1 = index2 then
                block2 - block1
            else        
                int (index2 - index1)
        comparer (snd a1) (snd a2)
    
    ContractHistory.get view dataAccess session contractId
    |> List.map (fun witnessPoint ->
        ContractData.get view dataAccess session witnessPoint,
        ContractConfirmations.get view dataAccess session witnessPoint
        |> getConfirmations account.blockNumber)
    |> List.sortWith comparer
    |> Wallet.Account.paginate skip take
    |> List.map (fun ((command, messageBody, txHash), (confirmations, _)) -> command, messageBody, txHash, confirmations)

let getContractAsset dataAccess session asset =
    ContractAssets.tryGet dataAccess session asset