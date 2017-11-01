module ServiceBusMessage.Tests

open Xunit
open FsUnit.Xunit
open FsNetMQ
open ServiceBusMessage


[<Fact>]
let ``send and recv Register``() =
    let msg = Register {
        service = "Life is short but Now lasts for ever";
    }

    use server = Socket.router ()
    Socket.bind server "inproc://Register.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://Register.test"

    ServiceBusMessage.send msg client

    Frame.recv server |> ignore
    let msg' = ServiceBusMessage.recv server

    msg' |> should equal (Some msg)

[<Fact>]
let ``Register size fits stream ``() =
    let register:Register = {
        service = "Life is short but Now lasts for ever";
    }

    let messageSize = Register.getMessageSize register

    let stream =
        Stream.create messageSize
        |> Register.write register

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Fact>]
let ``send and recv Command``() =
    let msg = Command {
        service = "Life is short but Now lasts for ever";
        payload = "Captcha Diem"B;
    }

    use server = Socket.dealer ()
    Socket.bind server "inproc://Command.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://Command.test"

    ServiceBusMessage.send msg server

    let msg' = ServiceBusMessage.recv client

    msg' |> should equal (Some msg)

[<Fact>]
let ``Command size fits stream ``() =
    let command:Command = {
        service = "Life is short but Now lasts for ever";
        payload = "Captcha Diem"B;
    }

    let messageSize = Command.getMessageSize command

    let stream =
        Stream.create messageSize
        |> Command.write command

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Fact>]
let ``send and recv RelayCommand``() =
    let msg = RelayCommand {
        payload = "Captcha Diem"B;
    }

    use server = Socket.dealer ()
    Socket.bind server "inproc://RelayCommand.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://RelayCommand.test"

    ServiceBusMessage.send msg server

    let msg' = ServiceBusMessage.recv client

    msg' |> should equal (Some msg)

[<Fact>]
let ``RelayCommand size fits stream ``() =
    let relaycommand:RelayCommand = {
        payload = "Captcha Diem"B;
    }

    let messageSize = RelayCommand.getMessageSize relaycommand

    let stream =
        Stream.create messageSize
        |> RelayCommand.write relaycommand

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Fact>]
let ``send and recv Request``() =
    let msg = Request {
        service = "Life is short but Now lasts for ever";
        payload = "Captcha Diem"B;
    }

    use server = Socket.dealer ()
    Socket.bind server "inproc://Request.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://Request.test"

    ServiceBusMessage.send msg server

    let msg' = ServiceBusMessage.recv client

    msg' |> should equal (Some msg)

[<Fact>]
let ``Request size fits stream ``() =
    let request:Request = {
        service = "Life is short but Now lasts for ever";
        payload = "Captcha Diem"B;
    }

    let messageSize = Request.getMessageSize request

    let stream =
        Stream.create messageSize
        |> Request.write request

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Fact>]
let ``send and recv RelayRequest``() =
    let msg = RelayRequest {
        sender = "Captcha Diem"B;
        payload = "Captcha Diem"B;
    }

    use server = Socket.dealer ()
    Socket.bind server "inproc://RelayRequest.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://RelayRequest.test"

    ServiceBusMessage.send msg server

    let msg' = ServiceBusMessage.recv client

    msg' |> should equal (Some msg)

[<Fact>]
let ``RelayRequest size fits stream ``() =
    let relayrequest:RelayRequest = {
        sender = "Captcha Diem"B;
        payload = "Captcha Diem"B;
    }

    let messageSize = RelayRequest.getMessageSize relayrequest

    let stream =
        Stream.create messageSize
        |> RelayRequest.write relayrequest

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Fact>]
let ``send and recv Response``() =
    let msg = Response {
        sender = "Captcha Diem"B;
        payload = "Captcha Diem"B;
    }

    use server = Socket.dealer ()
    Socket.bind server "inproc://Response.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://Response.test"

    ServiceBusMessage.send msg server

    let msg' = ServiceBusMessage.recv client

    msg' |> should equal (Some msg)

[<Fact>]
let ``Response size fits stream ``() =
    let response:Response = {
        sender = "Captcha Diem"B;
        payload = "Captcha Diem"B;
    }

    let messageSize = Response.getMessageSize response

    let stream =
        Stream.create messageSize
        |> Response.write response

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Fact>]
let ``send and recv RelayResponse``() =
    let msg = RelayResponse {
        payload = "Captcha Diem"B;
    }

    use server = Socket.dealer ()
    Socket.bind server "inproc://RelayResponse.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://RelayResponse.test"

    ServiceBusMessage.send msg server

    let msg' = ServiceBusMessage.recv client

    msg' |> should equal (Some msg)

[<Fact>]
let ``RelayResponse size fits stream ``() =
    let relayresponse:RelayResponse = {
        payload = "Captcha Diem"B;
    }

    let messageSize = RelayResponse.getMessageSize relayresponse

    let stream =
        Stream.create messageSize
        |> RelayResponse.write relayresponse

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Fact>]
let ``send and recv Ack``() =
    let msg = Ack {
        dummy = 123uy;
    }

    use server = Socket.dealer ()
    Socket.bind server "inproc://Ack.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://Ack.test"

    ServiceBusMessage.send msg server

    let msg' = ServiceBusMessage.recv client

    msg' |> should equal (Some msg)

[<Fact>]
let ``Ack size fits stream ``() =
    let ack:Ack = {
        dummy = 123uy;
    }

    let messageSize = Ack.getMessageSize ack

    let stream =
        Stream.create messageSize
        |> Ack.write ack

    let offset = Stream.getOffset stream

    messageSize |> should equal offset

[<Fact>]
let ``malformed message return None``() =
    use server = Socket.dealer ()
    Socket.bind server "inproc://ServiceBusMessage.test"

    use client = Socket.dealer ()
    Socket.connect client "inproc://ServiceBusMessage.test"

    Frame.send server "hello world"B

    let msg = ServiceBusMessage.recv client
    msg |> should equal None
 