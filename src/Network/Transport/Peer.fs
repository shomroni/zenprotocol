module Network.Transport.Peer

open FsNetMQ
open Network
open Infrastructure
open Logary.Message

module RoutingId =
    let toBytes (FsNetMQ.RoutingId.RoutingId bytes) = bytes
    let fromBytes bytes = FsNetMQ.RoutingId.RoutingId bytes

let version = 0ul;

// TODO: those should come from configuration?
// For now those are configured for fast unit testing
let pingInterval = System.TimeSpan.FromSeconds(60.0)
let pingTimeout = System.TimeSpan.FromSeconds(20.0)
let helloTimeout = System.TimeSpan.FromSeconds(5.0)

type CloseReason =
    | NoPingReply
    | Unreachable
    | PipeFull
    | UnknownMessage
    | UnknownPeer
    | NoHelloAck
    | ExpectingHelloAck
    | NoPong
    | IncorrectNetwork
    | BlockchainRequest
    | ConnectToSelf

type State =
    | Connecting of sent:System.DateTime
    | Active
    | Dead of reason:CloseReason

type PingState =
    | NoPing of lastPong: System.DateTime
    | WaitingForPong of nonce:uint32 * pingSent:System.DateTime

type PeerMode =
    | Connector of address:string
    | Listener

type Peer = {
    mode: PeerMode
    routingId: RoutingId.T;
    state: State;
    ping: PingState;
    networkId: uint32;
}

let private random = new System.Random()
let private getNonce () =
    let bytes = Array.create 4 0uy
    random.NextBytes (bytes)
    System.BitConverter.ToUInt32(bytes,0)

let private getNow () = System.DateTime.UtcNow

// TODO: check network on each message
// TODO: for each message we should minimum version

let state peer = peer.state
let isDead peer =
    match peer.state with
    | Dead _ -> true
    | _ -> false

let isActive peer = peer.state = Active
let isConntecting peer =
    match peer.state with
    | Connecting _ -> true
    | _-> false

let getAddress peer =
    match peer.mode with
    | Listener -> None
    | Connector address -> Some address

let private withState peer state = { peer with state =state; }

let private disconnect socket peer =
    match peer.mode with
    | Listener -> peer
    | Connector address ->
        Socket.disconnect socket (sprintf "tcp://%s" address)
        peer

let closePeer socket reason peer =
    eventX "Closing peer because of {reason}"
    >> setField "reason" (reason.ToString())
    |> Log.debug

    disconnect socket peer |> ignore
    withState peer (Dead reason)

let send socket peer msg =
    match RoutingId.trySet socket peer.routingId 0<milliseconds> with
    | RoutingId.TryResult.HostUnreachable -> closePeer socket Unreachable peer
    | RoutingId.TryResult.TimedOut -> closePeer socket PipeFull peer
    | RoutingId.TryResult.Ok ->
        Message.send socket msg
        peer

let private create mode routingId networkId state =
    {mode=mode; routingId = routingId; state = state; networkId=networkId; ping = NoPing (getNow ())}

let sendUpdateTimestamp next peer =
    match peer.mode with
    | Connector address ->
        InProcMessage.UpdateAddressTimestamp address
        |> next
    | _ -> ()

let connect socket networkId address randomPeerNonce =
    let routingId = Peer.connect socket (sprintf "tcp://%s" address)

    eventX "Connecting to {address}"
    >> setField "address" address
    |> Log.debug

    let peer = create (Connector address) routingId networkId (Connecting (getNow ()))

    // TODO: use correct values for this
    send socket peer (Message.Hello {version=version; network = networkId;nonce=randomPeerNonce})

let newPeer socket networkId next routingId msg randomPeerNonce =
    let createPeer = create Listener routingId networkId

    match msg with
    | None ->
        let peer = createPeer (Dead UnknownMessage)
        send socket peer (Message.UnknownMessage 0uy)
        |> disconnect socket
    | Some msg ->
        match msg with
        | Message.Hello hello ->
            if hello.network <> networkId then
                let peer = createPeer (Dead IncorrectNetwork)
                send socket peer (Message.IncorrectNetwork)
                |> disconnect socket
            else if hello.nonce = randomPeerNonce then
                createPeer (Dead ConnectToSelf)
                |> disconnect socket
            else
                eventX "Peer accepted"
                |> Log.info

                let peer = createPeer Active

                next (InProcMessage.Accepted (RoutingId.toBytes peer.routingId))

                send socket peer (Message.HelloAck {version=0ul; network = networkId;})
        | _ ->
            let peer = createPeer (Dead UnknownPeer)
            send socket peer (Message.UnknownPeer)
            |> disconnect socket

let handleConnectingState socket next peer msg =
    match msg with
    | None ->
        eventX "Received malformed message from peer"
        |> Log.warning

        send socket peer (Message.UnknownMessage 0uy)
        |> closePeer socket UnknownMessage
    | Some msg ->
        match msg with
        | Message.HelloAck helloAck ->
            if helloAck.network <> peer.networkId then
                closePeer socket IncorrectNetwork peer
            else
                // TODO: save version

                match peer.mode with
                | Connector address ->
                    
                    eventX "Connected to {address}"
                    >> setField "address" address
                    |> Log.info
                    
                    let peerId = RoutingId.toBytes peer.routingId
                    next (InProcMessage.Connected {address=address;peerId=peerId})
                | _ -> ()

                {peer with state=Active; ping=NoPing (getNow ())}
        | msg ->
            eventX "Expecting HelloAck but got {msg}"
            >> setField "msg" (sprintf "%A" msg)
            |> Log.debug
            closePeer socket ExpectingHelloAck peer

let handleActiveState socket next peer msg randomPeerNonce =
    match msg with
    | None ->
        eventX "Received malformed message from peer"
        |> Log.info

        send socket peer (Message.UnknownMessage 0uy)
        |> closePeer socket UnknownMessage
    | Some msg ->
        match msg with
        | Message.UnknownPeer _ ->
            eventX "Reconnecting to peer"
            |> Log.debug

            // NetMQ reconnection, just sending Hello again
            let peer = withState peer (Connecting (getNow ()))
            send socket peer (Message.Hello {version=version;network=peer.networkId;nonce=randomPeerNonce})
        | Message.Ping nonce ->
            // TODO: should we check when we last answer a ping? the other peer might try to spoof us
            sendUpdateTimestamp next peer
            send socket peer (Message.Pong nonce)
        | Message.Pong nonce ->
            match peer.ping with
            | NoPing _ -> peer
            | WaitingForPong (nonce',_) ->
                match nonce' = nonce with
                | true ->
                    sendUpdateTimestamp next peer
                    {peer with ping=NoPing (getNow ())}
                | false -> peer
        | Message.Transactions msg ->
            next (InProcMessage.Transactions {count=msg.count;txs=msg.txs})
            peer
        | Message.GetAddresses ->
            next (InProcMessage.GetAddresses (RoutingId.toBytes peer.routingId))
            peer
        | Message.Addresses addresses ->
            next (InProcMessage.Addresses {count=addresses.count;addresses=addresses.addresses})
            peer
        | Message.GetMemPool ->
            next (InProcMessage.GetMemPool (RoutingId.toBytes peer.routingId))
            peer
        | Message.MemPool txs ->
            next (InProcMessage.MemPool {peerId=(RoutingId.toBytes peer.routingId);txs=txs})
            peer
        | Message.GetTransactions txHashes ->
            next (InProcMessage.GetTransactions {peerId=(RoutingId.toBytes peer.routingId); txHashes=txHashes})
            peer
        | Message.GetBlock blockHash ->
            next (InProcMessage.BlockRequest {peerId=(RoutingId.toBytes peer.routingId); blockHash=blockHash})
            peer
        | Message.Block block ->
            next (InProcMessage.Block {peerId=(RoutingId.toBytes peer.routingId); block=block})
            peer
        | Message.Tip blockHeader ->
            next (InProcMessage.Tip {peerId=RoutingId.toBytes peer.routingId;blockHeader=blockHeader})
            peer
        | Message.NewBlock blockHeader ->
            next (InProcMessage.NewBlock {peerId=(RoutingId.toBytes peer.routingId); blockHeader=blockHeader})
            sendUpdateTimestamp next peer
            peer
        | Message.GetTip ->
            next (InProcMessage.GetTip (RoutingId.toBytes peer.routingId))
            peer
        | Message.GetHeaders request ->
            next (InProcMessage.HeadersRequest {
                peerId=(RoutingId.toBytes peer.routingId);
                from=request.from;
                endHash= request.endHash;
            })
            peer
        | Message.Headers headers ->
            next (InProcMessage.Headers {peerId=(RoutingId.toBytes peer.routingId);headers=headers})
            peer
        | Message.NewTransactions txHashes ->
            next (InProcMessage.NewTransactions {peerId=(RoutingId.toBytes peer.routingId);txHashes=txHashes})
            sendUpdateTimestamp next peer
            peer
        | _ ->
            // TODO: unexpected msg, close peer

            peer

let handleMessage socket next peer msg randomPeerNonce =
   match state peer with
   | Connecting _ -> handleConnectingState socket next peer msg
   | Active -> handleActiveState socket next peer msg randomPeerNonce
   | _ -> failwith "Dead peer should not receive any messages"

let handleTick socket peer =
    match peer.state with
    | Active ->
        match peer.ping with
        | NoPing lastPong ->
            match (getNow ()) - lastPong > pingInterval with
            | false -> peer
            | _ ->
                let nonce = getNonce ()
                let peer = send socket peer (Message.Ping nonce)
                match peer.state with
                | Active ->
                    {peer with ping=WaitingForPong (nonce, (getNow ()))}
                | _ -> peer
        | WaitingForPong (_,sentTime) ->
            match (getNow ()) - sentTime > pingTimeout with
            | false -> peer
            | true -> closePeer socket NoPong peer
    | Connecting sent ->
        match (getNow ()) - sent > helloTimeout with
        | false -> peer
        | true ->
            closePeer socket NoHelloAck peer
    | _ -> failwith "Dead peer should not receive any ticks requests"

let routingId peer = peer.routingId