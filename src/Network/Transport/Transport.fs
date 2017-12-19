module Network.Transport.Transport

open FSharp.Control.Reactive
open FsNetMQ
open Network
open Infrastructure
open Network.Transport

let timerInterval = 1 * 1000<milliseconds> // 10 seconds  

type T = 
    {
        inproc: Socket.T;
        actor: Actor.T;
    }
    interface System.IDisposable with
        member x.Dispose() = 
           Disposables.dispose x.actor
           Disposables.dispose x.inproc 

type Peers = Map<RoutingId.T,Peer.Peer>

let cleanDeadPeers inproc (peers:Peers) =

    Map.filter (fun _ -> Peer.isDead) peers
    |> Map.iter (fun _ peer ->
            match Peer.getAddress peer with
            | Some address -> 
                InProcMessage.send inproc (InProcMessage.Disconnected address)
            | None -> ())
            
    Map.filter (fun _ -> Peer.isDead >> not) peers            

let private handleTimer socket inproc (peers:Peers) = 
    let peers = Map.map (fun _ peer -> Peer.handleTick socket peer) peers
   
    cleanDeadPeers inproc peers

let private handleMessage socket inproc routingId msg (peers:Peers) = 
    let next msg = InProcMessage.send inproc msg
        
    let peer = 
        match Map.tryFind routingId peers with
        | Some peer -> Peer.handleMessage socket next peer msg
        | None -> Peer.newPeer socket next routingId msg
        
    let peers = Map.add routingId peer peers
        
    cleanDeadPeers inproc peers
    
let private handleInprocMessage socket inproc msg (peers:Peers) =
    match msg with 
    | None -> failwith "invalid inproc msg"
    | Some msg ->
        match msg with
        | InProcMessage.Connect address ->
            let peer = Peer.connect socket address
            let routingId = Peer.routingId peer
            
            Map.add routingId peer peers
        | InProcMessage.Transaction tx ->
            let peers = Map.map (fun _ peer -> 
                match Peer.isActive peer with
                | true ->  Peer.send socket peer (Message.Transaction tx) 
                | false -> peer) peers
            
            cleanDeadPeers inproc peers
        | msg -> failwithf "unexpected inproc msg %A" msg        
                
let private onError error = 
    Log.error "Unhandled exception from peer actor %A" error
    System.Environment.FailFast(sprintf "Unhandled exception peer actor" , error)

let connect transport address  = 
    InProcMessage.send transport.inproc (InProcMessage.Connect address)

let publishTransaction transport tx = 
    InProcMessage.send transport.inproc (InProcMessage.Transaction tx)
    
let recv transport =
    match InProcMessage.recv transport.inproc with
    | Some msg -> msg
    | None -> failwith "invalid inproc msg"
    
let tryRecv transport timeout = 
    match InProcMessage.tryRecv transport.inproc timeout with
    | Some msg -> Some msg
    | None -> None

let addToPoller poller transport = 
    Poller.addSocket poller transport.inproc
             
let create listen bind =
    let user,inproc = Pair.createPairs ()       

    let actor = FsNetMQ.Actor.create (fun shim ->          
        use poller = Poller.create ()
        use observer = Poller.registerEndMessage poller shim        
                
        use socket = Socket.peer ()                 
        if listen then
            Log.info "Listening on %s" bind
        
            let address = sprintf "tcp://%s" bind
            Socket.bind socket address
        
        let socketObservable =         
            Poller.addSocket poller socket
            |> Observable.map (fun _ -> 
                let routingId = RoutingId.get socket
                let msg =  Message.recv socket
                handleMessage socket inproc routingId msg)
            
        let timer = Timer.create timerInterval
        let timerObservable = 
                Poller.addTimer poller timer
                |> Observable.map (fun _ -> handleTimer socket inproc)
                
        let inprocObservable = 
            Poller.addSocket poller inproc
            |> Observable.map (fun _ ->
                let msg = InProcMessage.recv inproc
                handleInprocMessage socket inproc msg)               
                
        use observer =
            Observable.merge socketObservable timerObservable
            |> Observable.merge inprocObservable
            |> Observable.scanInit Map.empty (fun state handler -> handler state)
            |> Observable.subscribeWithError ignore onError
                            
        Actor.signal shim
        Poller.run poller
        
        Disposables.dispose inproc                
    )
    
    {actor=actor;inproc = user}    