﻿module Consensus.Tests.TransactionTests

open Consensus
open Consensus.Types
open Consensus.Hash
open Consensus.UtxoSet
open NUnit.Framework
open Crypto
open TransactionValidation

open TransactionNunitHelpers
open TransactionHelpers
open TestsInfrastructure.Nunit

let acs = ActiveContractSet.empty

let testInput1 = getInput 1uy 0ul
let testInput2 = getInput 2uy 0ul
let keys = getKeys 1

[<Test>]
let ``Transaction should be orphan``() =
    let orphanInput = {
        txHash = Hash (Array.create 32 100uy)
        index = 0ul
    }
    let output = { lock = PK testHash; spend = { asset = Constants.Zen; amount = 1UL } }
    let tx = {
        inputs = [ Outpoint testInput1; Outpoint orphanInput ]
        witnesses = []
        outputs = [ output ]
        contract = None
    }
    let utxos = Map.ofSeq [ testInput1, Unspent output ]
    inputsValidationOrphan 1ul acs utxos tx keys
    |> shouldEqual

[<Test>]
let ``Transaction basic validation should be Ok``() =
    let output = { lock = PK testHash; spend = { asset = Constants.Zen; amount = 1UL } }
    let tx = {
        inputs = [ Outpoint testInput1 ]
        witnesses = []
        outputs = [ output ]
        contract = None
    }
    basicValidationOk tx
    |> shouldEqual

[<Test>]
let ``Transaction validation should fail with outputs invalid error``() =
    let tx = {
        inputs = [ Outpoint testInput1 ];
        witnesses = []
        outputs = [ { lock = PK testHash; spend = { asset = Constants.Zen; amount = 0UL } } ]
        contract = None
    }
    basicValidationMsg "outputs invalid" tx
    |> shouldEqual

[<Test>]
let ``Transaction validation should fail with duplicate inputs error``() =
    let tx = {
        inputs = [ Outpoint testInput1; Outpoint testInput1 ]
        witnesses = []
        outputs = [ { lock = PK testHash; spend = { asset = Constants.Zen; amount = 1UL } } ]
        contract = None
    }
    basicValidationMsg "inputs duplicated" tx
    |> shouldEqual

let tx = { inputs = []; witnesses = []; outputs = []; contract = None }

[<Test>]
let ``Transaction validation should fail with structurally invalid contract data error``() =
    let msg = "structurally invalid contract data"
    { tx with contract = Some { code="";hints="";rlimit=0u;queries=0u } }
    |> basicValidationMsg msg
    |> shouldEqual
    { tx with contract = Some { code="x";hints="";rlimit=0u;queries=0u } }
    |> basicValidationMsg msg
    |> shouldEqual
    { tx with contract = Some { code="";hints="x";rlimit=0u;queries=0u } }
    |> basicValidationMsg msg
    |> shouldEqual
    { tx with contract = Some { code=null;hints="x";rlimit=0u;queries=0u } }
    |> basicValidationMsg msg
    |> shouldEqual
    { tx with contract = Some { code="x";hints=null;rlimit=0u;queries=0u } }
    |> basicValidationMsg msg
    |> shouldEqual

let invalidHash = Hash (Array.create (Hash.Length - 1) 1uy)

[<Test>]
let ``Transaction validation should fail with structurally invalid input data error``() =
    let msg = "structurally invalid input data"
    // test with malformatted outpoint
    { tx with inputs = [ Outpoint { txHash = invalidHash; index = 0ul } ] }
    |> basicValidationMsg msg
    |> shouldEqual
    // test with malformatted mint spend
    { tx with inputs = [ Mint { asset = invalidHash, Hash.zero; amount = 10UL } ] }
    |> basicValidationMsg msg
    |> shouldEqual

[<Test>]
let ``Transaction validation should fail with structurally invalid output data``() =
    let msg = "structurally invalid output data"
    // test with malformatted lock
    { tx with outputs = [ { lock = PK invalidHash; spend = { asset = Constants.Zen; amount = 10UL } } ] }
    |> basicValidationMsg msg
    |> shouldEqual
    // test with malformatted spend
    { tx with outputs = [ { lock = PK Hash.zero; spend = { asset = Hash.zero, invalidHash; amount = 10UL } } ] }
    |> basicValidationMsg msg
    |> shouldEqual
    // test with malformatted spend
    { tx with outputs = [ { lock = PK Hash.zero; spend = { asset = invalidHash, Hash.zero; amount = 10UL } } ] }
    |> basicValidationMsg msg
    |> shouldEqual

[<Test>]
let ``Signed transaction should be valid``() =
    let _, publicKey = keys.[0]
    let outputLock = PK (PublicKey.hash publicKey)
    let output = { lock = outputLock; spend = { asset = Constants.Zen; amount = 1UL } }
    let tx = {
        inputs = [ Outpoint testInput1 ]
        witnesses = []
        outputs = [ output ]
        contract = None
    }
    let utxos = Map.ofSeq [ testInput1, Unspent output ]
    inputsValidationOk 1ul acs utxos tx keys
    |> shouldEqual

[<Test>]
let ``Signed transaction validation result should be invalid witness``() =
    let outputLock = PK testHash // testHash will not match keypair
    let output = { lock = outputLock; spend = { asset = Constants.Zen; amount = 1UL } }
    let tx = {
        inputs = [ Outpoint testInput1 ]
        witnesses = []
        outputs = [ output ]
        contract = None
    }
    let utxos = Map.ofSeq [ testInput1, Unspent output ]
    inputsValidationMsg "PK witness mismatch" 1ul acs utxos tx keys
    |> shouldEqual