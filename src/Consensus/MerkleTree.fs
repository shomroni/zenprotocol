module Consensus.MerkleTree

let private findSplitIndex length =
    Seq.initInfinite (fun i -> pown 2 i)
    |> Seq.find (fun i -> i * 2 >= length)

let innerPrefix = [|105uy|] // 'i'

let rec computeRoot (xs:Hash.Hash list) =
    match xs with
    | head :: [] -> head
    | xs ->
        let splitIndex = findSplitIndex (List.length xs)

        let left,right = List.splitAt splitIndex xs

        let leftHash = computeRoot left
        let rightHash = computeRoot right

        Hash.computeMultiple (seq {yield innerPrefix;  yield (Hash.bytes leftHash); yield (Hash.bytes rightHash)})

let rec createAuditPath xs hashIndex =
    match xs with
    | _ :: [] -> []
    | xs ->
        let splitIndex = findSplitIndex (List.length xs)
        let left,right = List.splitAt splitIndex xs

        match hashIndex < splitIndex with
        | true ->
            let path = createAuditPath left hashIndex
            let rightHash = computeRoot right

            rightHash :: path
        | false ->
            let path = createAuditPath right (hashIndex - splitIndex)
            let leftHash = computeRoot left

            leftHash :: path

let verify root auditPath index hash =
    let rec verify' auditPath index =
        match auditPath with
        | [] -> hash
        | head :: tail ->
            let length = max (pown 2 (List.length auditPath)) (index + 1)
            let splitIndex = findSplitIndex length

            let leftHash,rightHash =
                match index < splitIndex with
                | true ->
                    let leftHash = verify' tail index
                    leftHash, head
                | false ->
                    let rightHash = verify' tail (index - splitIndex)
                    head,rightHash

            Hash.computeMultiple (seq {yield innerPrefix; yield (Hash.bytes leftHash); yield (Hash.bytes rightHash)})

    let root' = verify' auditPath index

    root' = root