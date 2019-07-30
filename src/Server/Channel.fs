module Channel

open System
open Saturn
open FSharp.Control.Tasks.V2
open System.Threading.Tasks
open Saturn.Channels
open Giraffe.Core
open Giraffe.HttpStatusCodeHandlers
open Microsoft.Extensions.Logging
open Thoth.Json.Net

type SocketToGame =
  { SocketId : Guid
    GameId : Guid }

let gameUpdate =
  channel {
    join (fun ctx id ->
      let _ =
        Task.Delay(300).ContinueWith(fun _ ->
            task {
              let hub = ctx.GetService<Channels.ISocketHub>()
              return! hub.SendMessageToClient "/ws" id "yourid" id
            })
      task {
        ctx.GetLogger().LogInformation(sprintf "User %A connected" id)
        return JoinResult.Ok
      })
    handle "game" (fun ctx msg ->
      let hub = ctx.GetService<Channels.ISocketHub>()
      let broadCast id msg =
        hub.SendMessageToClient "/ws" id "gameupdate" msg |> ignore
      task {
        let socketToGame =
          Decode.Auto.fromString<SocketToGame> (msg.Payload.ToString())
          |> Result.map
               (fun obj ->
               Persistence.subscribeToSocket obj.GameId (broadCast obj.SocketId))
        let str = sprintf "Game send %A" socketToGame
        ctx.GetLogger().LogInformation(str)
        printf "%s" str
        return ()
      })
  }
