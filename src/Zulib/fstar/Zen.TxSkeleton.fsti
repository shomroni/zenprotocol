module Zen.TxSkeleton

open Zen.Base
open Zen.Cost
open Zen.Types

module V = Zen.Vector
module U64 = FStar.UInt64

val getAvailableTokens: asset -> txSkeleton -> U64.t `cost` 64

val addInput: pointedOutput -> txSkeleton -> txSkeleton `cost` 64

val addInput_AvailableTokens:
    pOut:pointedOutput
    -> txSkel: txSkeleton
    -> Lemma ( let open U64 in
               let spend = (snd pOut).spend in
               let asset = spend.asset in
               let txSkel' = addInput pOut txSkel
                             |> force in
               let previouslyAvailableTokens = getAvailableTokens asset txSkel
                                               |> force in
               let availableTokens = getAvailableTokens asset txSkel'
                                     |> force in
               availableTokens = previouslyAvailableTokens +%^ spend.amount
             )


val addInputs(#n:nat):
  pointedOutput `V.t` n
  -> txSkeleton
  -> txSkeleton `cost` (64 * n + 64)

assume AddInputs_is_fold:
    forall (#n:nat) (pOuts: pointedOutput `V.t` n) (txSkel: txSkeleton).
        force (addInputs pOuts txSkel)
        ==
        force (V.foldl (flip addInput) txSkel pOuts)

val lockToContract:
  asset
  -> U64.t
  -> contractHash
  -> txSkeleton
  -> txSkeleton `cost` 64

val lockToPubKey:
  asset
  -> U64.t
  -> pkHash:hash
  -> txSkeleton
  -> txSkeleton `cost` 64

val lockToAddress:
  asset
  -> U64.t
  -> address:lock
  -> txSkeleton
  -> txSkeleton `cost` 64

val addChangeOutput:
  asset
  -> contractHash
  -> txSkeleton
  -> txSkeleton `cost` 64

val mint:
  amount:U64.t
  -> contractHash
  -> txSkeleton
  -> txSkeleton `cost` 64

val destroy:
  amount:U64.t
  -> contractHash
  -> txSkeleton
  -> txSkeleton `cost` 64

val fromWallet(#n:nat):
  asset ->
  amount:U64.t ->
  contractHash ->
  wallet n ->
  txSkeleton ->
  option txSkeleton `cost` (n * 128 + 192)

val isValid: txSkeleton -> bool `cost` 64
