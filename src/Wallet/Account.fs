module Wallet.Account

open Consensus
open Consensus.ChainParameters
open Consensus.Hash
open Consensus.Crypto
open Consensus.Types
open Infrastructure

let result = new Infrastructure.Result.ResultBuilder<string>()

type TransactionResult = Messaging.Services.TransactionResult

type OutputStatus = 
    | Spent of Output
    | Unspent of Output

type T = {
    outputs: Map<Outpoint, OutputStatus>
    mempool: (Hash.Hash*Transaction) list
    keyPair: KeyPair
    publicKeyHash: Hash
    tip: Hash.Hash
    blockNumber:uint32
}

let rootSecretKey = SecretKey [|189uy; 140uy; 82uy; 12uy; 79uy; 140uy; 35uy; 59uy; 11uy; 41uy; 199uy;
                           58uy; 23uy; 63uy; 112uy; 239uy; 45uy; 147uy; 51uy; 246uy; 34uy; 16uy;
                           156uy; 2uy; 111uy; 184uy; 140uy; 218uy; 136uy; 240uy; 57uy; 24uy |]

let create () = 
    let secretKey, publicKey = KeyPair.create ()

    {
        outputs = Map.empty
        keyPair = (secretKey, publicKey)
        publicKeyHash = PublicKey.hash publicKey
        mempool = List.empty
        tip = Hash.zero
        blockNumber = 0ul
    }

// update the outputs with transaction                                        
let private handleTransaction txHash (tx:Transaction) account outputs =
    
    let handleOutput outputs (index,output) = 
        match output.lock with
        | Coinbase (_,pkHash) when pkHash = account.publicKeyHash ->
             let outpoint = {txHash=txHash;index=index;}
             Map.add outpoint (Unspent output) outputs
        | PK pkHash when pkHash = account.publicKeyHash ->              
            let outpoint = {txHash=txHash;index=index;}
            Map.add outpoint (Unspent output) outputs
        | _ -> outputs
        
    let handleInput outputs outpoint = 
        match Map.tryFind outpoint outputs with
        | Some (Unspent output) -> 
            Map.add outpoint (Spent output) outputs
        | _ -> outputs
           
    let outputs = List.fold handleInput outputs tx.inputs         
    
    let outputsWithIndex = List.mapi (fun i output -> (uint32 i,output)) tx.outputs
    List.fold handleOutput outputs outputsWithIndex          
    
let handleBlock blockHash block account =
    let txHashes = List.map (fun tx -> Transaction.hash tx) block.transactions     
    let transactions = List.zip txHashes block.transactions        
 
    let outputs = 
        List.fold (fun outputs (txHash,tx) -> handleTransaction txHash tx account outputs) 
            account.outputs transactions
    
    // remove all transactions from list        
    let set = Set.ofList txHashes   
    let mempool = List.reject (fun (txHash,_) -> Set.contains txHash set) account.mempool            
                                        
    {account with tip = blockHash; blockNumber = block.header.blockNumber; mempool=mempool;outputs = outputs}     
    
let undoBlock block account = 
    let undoTransaction tx (outputs,mempool) = 
        let txHash = Transaction.hash tx
        
        let handleOutput outputs (index,output) = 
            match output.lock with
            | Coinbase (_,pkHash) when pkHash = account.publicKeyHash ->
                let outpoint = {txHash=txHash;index=index;}
                Map.remove outpoint outputs                
            | PK pkHash when pkHash = account.publicKeyHash ->
                let outpoint = {txHash=txHash;index=index;}
                Map.remove outpoint outputs                
            | _ -> outputs                 
            
        let handleInput outputs input = 
            match Map.tryFind input outputs with
            | Some (Spent output) -> Map.add input (Unspent output) outputs
            | _ -> outputs
            
        let outputs' = List.fold handleInput outputs tx.inputs         
            
        let outputsWithIndex = List.mapi (fun i output -> (uint32 i,output)) tx.outputs
        let outputs' = List.fold handleOutput outputs' outputsWithIndex        
        
        let mempool = 
            // adding the transaction back to mempool only if the transaction affected the account
            if outputs' <> outputs then                                
                (txHash,tx) :: mempool
            else 
                mempool
        
        outputs',mempool
        
    let outputs,mempool = List.foldBack undoTransaction block.transactions (account.outputs,account.mempool) 
    
    {account with tip=block.header.parent;blockNumber=block.header.blockNumber - 1ul; outputs=outputs;mempool = mempool} 
   
let sync chain tipBlockHash (getHeader:Hash.Hash -> BlockHeader) (getBlock:Hash.Hash -> Block) account= 
    let accountTipBlockNumber = 
        if account.tip <> Hash.zero then 
            (getHeader account.tip).blockNumber
        else 
            0ul
    
    // In case the account is not in the main chain, we are looking for the fork block
    // between the account chain and main chain, undo from account chain up to the fork block and then 
    // redo main chain up to the tip
    let rec reorg currentBlockHash account = 
        if account.tip = currentBlockHash then
            // we found the fork block
            account
        else 
            let oldBlock = getBlock account.tip
            let newBlock = getBlock currentBlockHash
                            
            account                            
            |> undoBlock oldBlock 
            |> reorg newBlock.header.parent
            |> handleBlock currentBlockHash newBlock
                
    let rec sync' currentBlockHash =
        // If new account and genesis block handle genesis block      
        if account.tip = Hash.zero && currentBlockHash = ChainParameters.getGenesisHash chain then
            let block = getBlock currentBlockHash    
            handleBlock currentBlockHash block account          
        // If we found the account tip we stop the recursion and start syncing up      
        elif currentBlockHash = account.tip then
            account
        else 
            let block = getBlock currentBlockHash
                                                  
            if block.header.blockNumber = accountTipBlockNumber then
                // The account is not in the main chain, we have to start a reorg
                reorg currentBlockHash account
            else 
                // continue looking for the account tip in the chain and then handling the current block              
                sync' block.header.parent            
                |> handleBlock currentBlockHash block
                
    sync' tipBlockHash

let getUnspentOutputs account = 
    // we first update the account according to the mempool transactions
    List.fold (fun outputs (txHash,tx) -> 
        handleTransaction txHash tx account outputs) account.outputs account.mempool
    |> Map.filter (fun _ output ->
        match output with
        | Unspent _-> true
        | _ -> false)
    |> Map.map (fun _ output ->
        match output with 
        | Unspent output -> output
        | _ -> failwith "unexpected")   
               
let addTransaction txHash (tx:Transaction) account =    
    // check if the transaction relevant to the account
    let anyOutput = List.exists (fun output -> 
        match output.lock with
        | PK pkHash when pkHash = account.publicKeyHash -> true
        | Coinbase (_,pkHash) when pkHash = account.publicKeyHash -> true
        | _ -> false) tx.outputs
    
    let unspentOutputs = getUnspentOutputs account
    let anyInputs = List.exists (fun input -> Map.containsKey input unspentOutputs) tx.inputs
 
    if anyInputs || anyOutput then 
        let mempool = List.add (txHash,tx) account.mempool
    
        {account with mempool = mempool}
    else
        account
           
let getBalance account =
    let unspent = getUnspentOutputs account
                
    Map.fold (fun balance _ output ->
        match Map.tryFind output.spend.asset balance with
        | Some amount -> Map.add output.spend.asset (amount+output.spend.amount) balance
        | None -> Map.add output.spend.asset output.spend.amount balance) Map.empty unspent
     
let getAddress chain account =
    Address.encode chain (Address.PK account.publicKeyHash)

let private getInputs account spend =
    let unspent = getUnspentOutputs account
 
    let collectInputs ((inputs, keys), collectedAmount) outpoint output =
        let isMature = 
            match output.lock with 
            | Coinbase (blockNumber,_) -> account.blockNumber - blockNumber >= CoinbaseMaturity
            | _ -> true 
    
        if isMature && spend.amount > collectedAmount && spend.asset = output.spend.asset then
            (((outpoint,output) :: inputs, account.keyPair :: keys), output.spend.amount + collectedAmount)
        else 
            ((inputs, keys), collectedAmount)
            
    let (inputs, keys), collectedAmount = Map.fold collectInputs (([],[]),0UL) unspent
     
    if collectedAmount >= spend.amount then
        Ok (inputs,keys,collectedAmount)
    else
        Error "Not enough tokens"        
     
let createTransaction chain account pkHash spend = result  {
    let! (inputs, keys, amount) = getInputs account spend
    let inputPoints = List.map fst inputs
    let outputs =
        {spend=spend;lock=PK pkHash}
     :: if amount = spend.amount then [] else
        // Create change if needed
        [{
            spend={spend with amount=(amount-spend.amount)};
            lock=PK account.publicKeyHash
        }]
    return
        Transaction.sign keys { inputs=inputPoints;
                                outputs=outputs;
                                witnesses=[];
                                contract=None }
            
    }
                       
let createActivateContractTransaction account code =
    let unspent = getUnspentOutputs account
    result {
    let! hints = Contract.recordHints code
    let input, output = Map.toSeq unspent |> Seq.head
    let output' = {output with lock=PK account.publicKeyHash}
    return Transaction.sign
        [ account.keyPair ]
        {
            inputs=[ input ];
            outputs=[ output' ];
            witnesses=[];
            contract = Some (code, hints)
        }
    }

let rootAccount =                     
    {
        outputs = Map.empty
        keyPair = KeyPair.fromSecretKey rootSecretKey
        publicKeyHash = Transaction.rootPKHash
        tip = Hash.zero
        mempool= List.empty
        blockNumber=0ul
    }  

let createTestAccount () =                            
    rootAccount
    |> addTransaction Transaction.rootTxHash Transaction.rootTx
                 

let createExecuteContractTransaction account executeContract cHash command spends = result {
    let mutable txSkeleton = TxSkeleton.empty
    let mutable keys = List.empty
    for (asset, amount) in Map.toSeq spends do
        let! inputs, keys', collectedAmount =
            getInputs account {asset=asset;amount=amount}
        txSkeleton <-
            TxSkeleton.addInputs inputs txSkeleton
            |> TxSkeleton.addChange asset collectedAmount amount account.publicKeyHash
        keys <- List.append keys keys'
    let! unsignedTx = executeContract cHash command (PK account.publicKeyHash) txSkeleton
    return Transaction.sign keys unsignedTx
    }
                        