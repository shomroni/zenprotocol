module Blockchain.State

open Consensus

   
type TipState =
    {
        tip: ExtendedBlockHeader.T
        activeContractSet: ActiveContractSet.T
        ema: EMA.T
    }

type MemoryState =
    {
        utxoSet: UtxoSet.T
        activeContractSet: ActiveContractSet.T
        orphanPool: OrphanPool.T
        mempool: MemPool.T
        contractCache: ContractCache.T
        contractStates: ContractStates.T
        invalidTxHashes: Set<Hash.Hash>
    }

type State =
    {
        tipState: TipState
        memoryState: MemoryState
        initialBlockDownload:InitialBlockDownload.T
        headers: uint32
        cgp: CGP.T
    }
