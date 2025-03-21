// Modification to src/Consensus/Block.fs

module Consensus.Block

open Consensus
open Types
open Infrastructure
open Serialization
open Chain
open Consensus.Serialization.Serialization

[<Literal>]
let HeaderSize =
    100

let TwoPow256 =
    bigint.Pow (2I, 256)

let MaxTimeInFuture =
    15UL * 60UL * 1000UL // 15 minutes in milliseconds

let genesisParent =
    {
        version     = Version0
        parent      = Hash.zero
        blockNumber = 0ul
        commitments = Hash.zero
        timestamp   = 0UL
        difficulty  = 0ul
        nonce       = (0UL, 0UL)
    }

// CGP2 wallet address: zen1qqcarpd4cqrzy4rx7se9fwtmlq8puruc98yz5ctd46f0xcsxwqenqfl8phh
// We need to extract the PK hash from this address
let cgp2PkHash =
    FsBech32.Zen.decode "zen1qqcarpd4cqrzy4rx7se9fwtmlq8puruc98yz5ctd46f0xcsxwqenqfl8phh"
    |> Option.map (fun (_, data) -> Hash.Hash data)
    |> Option.defaultValue Hash.zero // Fallback to zero hash if address is invalid

// Define the amount to airdrop to CGP2 in each block (10% of block reward)
let cgp2AirdropPercentage = 10UL

let computeCommitmentsRoot : Hash.Hash list -> Hash.Hash =
    MerkleTree.computeRoot

let hash : BlockHeader -> Hash.Hash =
    Header.serialize
    >> Hash.compute

let toHex : Block -> string =
    Block.serialize
    >> FsBech32.Base16.encode

let fromHex : string -> Block option =
    FsBech32.Base16.decode
    >> Option.bind Block.deserialize

let isGenesis
    ( chain : Chain.ChainParameters )
    ( block : Block                 )
    : bool =
    
    let blockHashHash =
        block.header
        |> hash
        |> Hash.computeOfHash
    
    chain.genesisHashHash = blockHashHash

let getChainWork
    ( prevWork : bigint      )
    ( header   : BlockHeader )
    : bigint =
    
    let target =
        header.difficulty
        |> Difficulty.uncompress
        |> Hash.toBigInt
    
    let proof =
        bigint.Divide (TwoPow256, target + 1I)
    
    prevWork + proof

let createGenesis
    ( chain        : Chain.ChainParameters    )
    ( transactions : TransactionExtended list )
    ( nonce        : Nonce                    )
    : Block =
    
    let txMerkleRoot =
        transactions
        |> List.map (fun tx -> tx.txHash)
        |> MerkleTree.computeRoot
    
    let witnessMerkleRoot =
        transactions
        |> List.map (fun tx -> tx.witnessHash)
        |> MerkleTree.computeRoot
    
    let acsMerkleRoot =
        ActiveContractSet.root ActiveContractSet.empty
    
    let commitments =
        Block.createCommitments txMerkleRoot witnessMerkleRoot acsMerkleRoot []
        |> computeCommitmentsRoot
    
    let header =
        {
            version     = Version0
            parent      = Hash.zero
            blockNumber = 1ul
            commitments = commitments
            timestamp   = chain.genesisTime
            difficulty  = (EMA.create chain).difficulty
            nonce       = nonce
        }

    {
        header                      = header
        transactions                = transactions
        commitments                 = []
        txMerkleRoot                = txMerkleRoot
        witnessMerkleRoot           = witnessMerkleRoot
        activeContractSetMerkleRoot = acsMerkleRoot
    }

let getBlockSacrificeAmount
    ( chainParams : ChainParameters     )
    ( acs         : ActiveContractSet.T )
    : uint64 =
    
    let computeContractSacrifice (contract : Contract.T) =
        (uint64 <| String.length contract.code) * chainParams.contractSacrificePerBytePerBlock
    
    acs
    |> ActiveContractSet.getContracts
    |> Seq.sumBy computeContractSacrifice

let getCgpCoinbase
    ( chainParams : ChainParameters )
    ( blockNumber : uint32          )
    ( cgp         : CGP.T           )
    : Output list =
    
    if blockAllocation blockNumber cgp.allocation <> 0UL then
            [{
                lock  = Contract chainParams.cgpContractId
                spend =
                    {
                        asset  = Asset.Zen
                        amount = blockAllocation blockNumber cgp.allocation
                    }
            }]
        else
            []

// New function to create CGP2 airdrop output
let getCgp2Airdrop
    ( chainParams : ChainParameters )
    ( blockNumber : uint32          )
    ( cgp         : CGP.T           )
    : Output list =
    
    // Calculate 10% of the block reward for CGP2
    let blockRewardAmount = blockReward blockNumber cgp.allocation
    let cgp2Amount = (blockRewardAmount * cgp2AirdropPercentage) / 100UL
    
    if cgp2Amount > 0UL then
        [{
            lock = PK cgp2PkHash  // Use the wallet's PK hash
            spend = {
                asset = Asset.Zen
                amount = cgp2Amount
            }
        }]
    else
        []

let getBlockCoinbase
    ( chainParams    : ChainParameters          )
    ( acs            : ActiveContractSet.T      )
    ( blockNumber    : uint32                   )
    ( transactions   : TransactionExtended list )
    ( coinbasePkHash : Hash.Hash                )
    ( cgp            : CGP.T                    )
    : TransactionExtended =
    
    let blockFees =
        transactions
        |> List.collect (fun ex -> ex.tx.outputs)
        |> Fund.accumulateSpends
               (function | { lock=Fee; spend=spend } -> Some spend | _ -> None)
    
    let blockRewardAndFees =
        let totalZen       = Fund.find Asset.Zen blockFees
        let blockSacrifice = getBlockSacrificeAmount chainParams acs
        
        // Calculate base miner reward (reduced by CGP2 airdrop percentage)
        let baseReward = blockReward blockNumber cgp.allocation
        let minerReward = baseReward * (100UL - cgp2AirdropPercentage) / 100UL

        Map.add Asset.Zen (totalZen + minerReward + blockSacrifice) blockFees
    
    // Get the coinbase outputs by summing the fees per asset and adding the block reward
    let coinbaseOutputs =
        Map.toSeq blockRewardAndFees
        |> Seq.map
            begin fun (asset, amount) ->
            {
                lock  = Coinbase (blockNumber, coinbasePkHash)
                spend = { asset=asset; amount=amount }
            }
            end
        |> Seq.toList
    
    // Add CGP and CGP2 outputs
    let outputs = coinbaseOutputs @ getCgpCoinbase chainParams blockNumber cgp @ getCgp2Airdrop chainParams blockNumber cgp
    
    let tx = {
        version   = Version0
        inputs    = []
        outputs   = outputs
        contract  = None
        witnesses = []
    }
    
    Transaction.toExtended tx

let createTemplate
    ( chainParams    : ChainParameters          )
    ( parent         : BlockHeader              )
    ( timestamp      : uint64                   )
    ( ema            : EMA.T                    )
    ( acs            : ActiveContractSet.T      )
    ( cgp            : CGP.T                    )
    ( transactions   : TransactionExtended list )
    ( coinbasePkHash : Hash.Hash                )
    : Block =
    
    let blockNumber =
        parent.blockNumber + 1ul
    
    let coinbase =
        getBlockCoinbase chainParams acs blockNumber transactions coinbasePkHash cgp
    
    let transactions =
        coinbase :: transactions
    
    let txMerkleRoot =
        transactions
        |> List.map (fun tx-> tx.txHash)
        |> MerkleTree.computeRoot
    
    let witnessMerkleRoot =
        transactions
        |> List.map (fun tx-> tx.witnessHash)
        |> MerkleTree.computeRoot
    
    let acs =
        ActiveContractSet.expireContracts blockNumber acs
    
    let acsMerkleRoot =
        ActiveContractSet.root acs
    
    let parentHash =
        hash parent
    
    let median =
        EMA.earliest ema
    
    let timestamp =
        if timestamp > median then timestamp else (median + 1UL)
    
    // TODO: add utxo commitments
    let commitments =
        Block.createCommitments txMerkleRoot witnessMerkleRoot acsMerkleRoot []
        |> computeCommitmentsRoot
    
    let header =
        {
            version     = Version1
            parent      = parentHash
            blockNumber = blockNumber
            commitments = commitments
            timestamp   = timestamp
            difficulty  = ema.difficulty
            nonce       = 0UL,0UL
        }
    
    {
        header                      = header
        transactions                = transactions
        commitments                 = []
        txMerkleRoot                = txMerkleRoot
        witnessMerkleRoot           = witnessMerkleRoot
        activeContractSetMerkleRoot = acsMerkleRoot
    }
