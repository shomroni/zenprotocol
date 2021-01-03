module Z60de85a214850bf6192e7c4416fbccaf3cfcc0a927dd6b770a70ff08d24046d3

open Zen.Types
open Zen.Base
open Zen.Cost

module RT = Zen.ResultT
module Tx = Zen.TxSkeleton
module C = Zen.Cost

let main txSkeleton _ contractId command sender messageBody wallet state =
  (Zen.Cost.inc 22
      (let! asset = Zen.Asset.getDefault contractId in
        let spend = { asset = asset; amount = 1000uL } in
        let pInput = Mint spend in
        let! txSkeleton =
          Tx.addInput pInput txSkeleton >>= Tx.lockToContract spend.asset spend.amount contractId in
        RT.ok @ { tx = txSkeleton; message = None; state = NoChange }))

let cf _ _ _ _ _ _ _ = (Zen.Cost.inc 13 (64 + (64 + 64 + 0) + 22 |> cast nat |> C.ret))
val mainFunction: Zen.Types.mainFunction
let mainFunction = Zen.Types.MainFunc (Zen.Types.CostFunc cf) main