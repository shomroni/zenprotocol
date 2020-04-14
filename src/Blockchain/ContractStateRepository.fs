module Blockchain.ContractStateRepository

open Blockchain
open Consensus
open DataAccess
open DatabaseContext

let get (session:Session) contract =
    Collection.tryGet session.context.contractStates session.session contract 
    
let save (session:Session) (states:ContractStates.T) =
    let collection = session.context.contractStates
    let session = session.session

    states
    |> Map.iter (fun contractId ->
        function
        | None ->
            Collection.delete collection session contractId
        | Some contractState ->
            Collection.put collection session contractId contractState)