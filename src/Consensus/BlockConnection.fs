// Modification to src/Consensus/BlockConnection.fs

module Consensus.BlockConnection

open Consensus
open Types
open Infrastructure
open Result
open Serialization
open Chain
open Consensus.Serialization.Serialization
open Functional
open Block

// Static connection environment - doesn't change during connection 
type Env =
    {
        chainParams      : ChainParameters
        timestamp        : uint64
        getUTXO          : Outpoint -> UtxoSet.OutputStatus
        getContractState : ContractId -> Zen.Types.Data.data option
        contractsPath    : string
        parent           : BlockHeader
        block            : Block
    }

// Dynamic connection state - changes during connection
type State =
    {
        utxoSet        : UtxoSet.T
        acs            : ActiveContractSet.T
        cgp            : CGP.T
        ema            : EMA.T
        contractCache  : ContractCache.T
        contractStates : ContractStates.T
    }

type Check = State -> Env -> Result<State, string>

module BlockNumber =
    
    let check
        : Check = fun state env ->
        
        if env.parent.blockNumber + 1ul <> env.block.header.blockNumber then
            Error "blockNumber mismatch"
        else
            Ok state

module Difficulty =
    
    let check
        : Check = fun state env ->
        
        let nextEma =
            EMA.add env.chainParams env.block.header.timestamp state.ema
        
        if env.block.header.difficulty <> state.ema.difficulty            then
            Error "incorrect proof of work"
        elif isGenesis env.chainParams env.block                          then
            Ok { state with ema=nextEma }
        elif env.block.header.timestamp <= EMA.earliest state.ema         then
            Error "block's timestamp is too early"
        elif env.block.header.timestamp > env.timestamp + MaxTimeInFuture then
            Error "block timestamp too far in the future"
        else
            Ok { state with ema=nextEma }

module TxInputs =
    
    let private validateInContext
        ( env   : Env                 )
        ( state : State               )
        ( ex    : TransactionExtended )
        : Result<State, string> = result {
            
            let! _, acs, contractCache, contractStates =
                TransactionValidation.validateInContext
                    env.chainParams
                    env.getUTXO
                    env.contractsPath
                    env.block.header.blockNumber
                    env.block.header.timestamp
                    state.acs
                    state.contractCache
                    state.utxoSet
                    env.getContractState
                    state.contractStates
                    ex
                |> Result.mapError (sprintf "transactions failed inputs validation due to %A")
            
            return
                { state with
                     utxoSet        = UtxoSet.handleTransaction env.getUTXO ex.txHash ex.tx state.utxoSet
                     acs            = acs
                     contractCache  = contractCache
                     contractStates = contractStates
                }
        }
    
    let check
        : Check = fun state env ->
        
        let updateUtxoSet utxoSet ex = UtxoSet.handleTransaction env.getUTXO ex.txHash ex.tx utxoSet
        
        if isGenesis env.chainParams env.block then
            Ok
                { state with
                     utxoSet = List.fold updateUtxoSet state.utxoSet env.block.transactions
                     cgp     = CGP.empty
                }
        else
            let coinbase        = List.head env.block.transactions
            let withoutCoinbase = List.tail env.block.transactions
            
            let initState =
                Ok { state with utxoSet = updateUtxoSet state.utxoSet coinbase }
            
            foldM (validateInContext env) initState withoutCoinbase

module Coinbase =
    
    // CGP2 airdrop percentage - must match the value in Block.fs
    let cgp2AirdropPercentage = 10UL

    // CGP2 wallet address (must match Block.fs)
    let cgp2PkHash =
        FsBech32.Zen.decode "zen1qqcarpd4cqrzy4rx7se9fwtmlq8puruc98yz5ctd46f0xcsxwqenqfl8phh"
        |> Option.map (fun (_, data) -> Hash.Hash data)
        |> Option.defaultValue Hash.zero
    
    let private computeblockRewardAndFees
        ( outputs : Output list )
        ( state   : State       )
        ( env     : Env         )
        : Fund.T =
        
        // Compute entire fees paid transactions in the block
        let blockFees =
            outputs |> Fund.accumulateSpends
               (function | {lock = Fee; spend=spend} -> Some spend | _ -> None)
        
        let totalZen =
            Fund.find Asset.Zen blockFees
        
        let blockSacrifice =
            getBlockSacrificeAmount env.chainParams state.acs
        
        // Total original block reward
        let originalBlockReward =
            blockReward env.block.header.blockNumber state.cgp.allocation
        
        // Miner reward (90% of original block reward)
        let minerReward =
            originalBlockReward * (100UL - cgp2AirdropPercentage) / 100UL
        
        let allocationReward =
            blockAllocation env.block.header.blockNumber state.cgp.allocation
        
        // Add the fees, sacrifices, and reduced miner reward
        Map.add Asset.Zen (totalZen + blockSacrifice + minerReward + allocationReward) blockFees
    
    let private splitOutputs
        ( block : Block)
        : Result<Output list * Output list, string> =
            
        match block.transactions with
        | coinbase::transactions ->
            Ok (coinbase.tx.outputs, List.collect (fun ex -> ex.tx.outputs) transactions)
        | [] ->
            Error "no coinbase tx"
    
    let check
        : Check = fun state env -> result {
        
        if isGenesis env.chainParams env.block then
            return state
        else
            let! coinbaseOutputs, otherOutputs = splitOutputs env.block
            
            // Extract CGP2 outputs
            let cgp2Outputs =
                coinbaseOutputs |> Fund.accumulateSpends
                    begin function
                    | {lock = PK pkHash; spend=spend} when pkHash = cgp2PkHash ->
                        Some spend
                    | _ ->
                        None
                    end
            
            // Compute the amount of reward per asset
            let coinbaseTotals =
                coinbaseOutputs |> Fund.accumulateSpends
                    (fun output -> Some output.spend)
            
            // Compute the block reward and fees together
            let blockRewardAndFees =
                computeblockRewardAndFees otherOutputs state env
            
            // Extract CGP contract outputs
            let cgpAmounts =
                coinbaseOutputs |> Fund.accumulateSpends
                    begin function
                    | {lock = Contract contractId; spend=spend} when contractId = env.chainParams.cgpContractId ->
                        Some spend
                    | _ ->
                        None
                    end
            
            // Get all contract outputs - should only be CGP contract
            let allContractAmounts =
                coinbaseOutputs |> Fund.accumulateSpends
                    (function | {lock = Contract _; spend=spend} -> Some spend | _ -> None)
            
            // CGP Zen amount
            let cgpZenAmount =
                Fund.find Asset.Zen cgpAmounts
            
            // CGP2 Zen amount (should be 10% of the block reward)
            let cgp2ZenAmount =
                Fund.find Asset.Zen cgp2Outputs
            
            // Calculate expected CGP2 amount (10% of original block reward)
            let originalBlockReward = blockReward env.block.header.blockNumber state.cgp.allocation
            let expectedCgp2Amount = (originalBlockReward * cgp2AirdropPercentage) / 100UL
            
            // Verify that the coinbase total (miner + CGP + CGP2) matches the expected amount
            if coinbaseTotals <> blockRewardAndFees then
                return! Error "block reward is incorrect"
            // Verify that all contract outputs are going to the CGP contract
            elif allContractAmounts <> cgpAmounts then
                return! Error "reward to cgp contract in invalid"
            // Verify that the CGP amount is correct
            elif cgpZenAmount <> (blockAllocation env.block.header.blockNumber state.cgp.allocation) then
                return! Error "cgp reward is not correct"
            // Verify that the CGP2 amount is correct (10% of original block reward)
            elif cgp2ZenAmount <> expectedCgp2Amount && expectedCgp2Amount > 0UL then
                return! Error "cgp2 reward is not correct"
            else
                return state
        }

module Commitments =
    
    let check
        : Check = fun state env ->
        
        let acs =
            ActiveContractSet.expireContracts env.block.header.blockNumber state.acs
        
        let acsMerkleRoot =
            ActiveContractSet.root acs
        
        // we already validated txMerkleRoot and witness merkle root at the basic validation,
        // re-calculate with acsMerkleRoot
        let commitments =
            Block.createCommitments
                env.block.txMerkleRoot
                env.block.witnessMerkleRoot
                acsMerkleRoot
                env.block.commitments
            |> computeCommitmentsRoot
        
        // We ignore the known commitments in the block as we already calculated them
        // Only check that the final commitment is correct
        if commitments = env.block.header.commitments then
            Ok { state with acs = acs }
        else
            Error "commitments mismatch"

module Weight =
    
    let check
        : Check = fun state env -> result {
        
        if CGP.Connection.isPayoutTransactionInBlock env.chainParams env.block then
            // no need to check the other transactions as the transaction validation prevents big transactions
            return state
        else 
            let! weight =
                Weight.blockWeight env.getUTXO env.block state.utxoSet
            
            let maxWeight =
                env.chainParams.maxBlockWeight
            
            if weight <= maxWeight then
                return state
            else
                return! Error "block weight exceeds maximum"
        }

module PayoutTx =
    
    let check
        : Check = fun state env ->
        
        if CGP.isPayoutBlock env.chainParams env.block.header.blockNumber then
            CGP.Connection.checkPayoutWitness env.chainParams env.block.transactions state.cgp
        else
            Ok ()    // The CGP contract ensures there is never payout Tx outside the payout block
        
        |> Result.map (konst state)

let private pairBlockState
    (state : State)
    : ReaderResult.ReaderResult<Env, string, Block * State> = ReaderResult.readerResult {
        let! env = ReaderResult.ask
        return (env.block, state)
    }

let connect
    ( state : State )
    ( env   : Env   )
    : Result<Block * State, string> =
    
    let (>>=) = ReaderResult.(>>=)
    
    ReaderResult.ret state
    >>= BlockNumber .check
    >>= Difficulty  .check
    >>= Weight      .check
    >>= PayoutTx    .check
    >>= TxInputs    .check
    >>= Coinbase    .check
    >>= Commitments .check
    >>= pairBlockState
    |> ReaderResult.eval env
