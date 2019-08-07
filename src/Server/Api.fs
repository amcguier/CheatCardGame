module Api

open System
open System.Threading.Tasks
open FSharp.Control.Tasks.V2
open Giraffe
open Saturn
open Shared
open Thoth.Json.Net
open System.Collections.Generic
open Persistence

let getInitCounter() : Task<Counter> = task { return { Value = 42 } }

let resultTo404 ctx msg =
  function
  | Ok(game) -> Controller.json ctx game
  | Error(_) -> (Response.notFound ctx msg)

let successOrBadReq ctx onSuccess =
  function
  | Ok(obj) -> onSuccess obj
  | Error(str) -> Response.badRequest ctx str

let successOr404 ctx onSuccess =
  function
  | Ok(obj) -> onSuccess obj
  | Error(str) -> Response.notFound ctx str

let passController gameId playerId =
  controller
    {
    create
      (fun ctx ->
      task
        {
        return! makePlay gameId playerId Pass
                |> successOrBadReq ctx (Controller.json ctx) }) }
let callController gameId playerId =
  controller
    {
    create
      (fun ctx ->
      task
        {
        return! makePlay gameId playerId Call
                |> successOrBadReq ctx (Controller.json ctx) }) }
let playController gameId playerId =
  controller
    {
    create
      (fun ctx ->
      task
        {
        let! (cards : Card list) = Controller.getJson ctx
        return! makePlay gameId playerId (Cards(cards))
                |> successOrBadReq ctx (Controller.json ctx) }) }
let playerTurnController gameId playerId =
  controller
    {
    index
      (fun ctx ->
      task
        {
        return! showGame gameId
                |> successOr404 ctx
                     (getCurrentTurn
                      >> successOrBadReq ctx (Controller.json ctx)) }) }

let playerController gameId =
  controller {
    subController "/turns" (playerTurnController gameId)
    subController "/turns/pass" (passController gameId)
    subController "/turns/call" (callController gameId)
    subController "/turns/play" (playController gameId)
    index (fun ctx ->
      showGame gameId
      |> successOr404 ctx (getPlayers
                           >> (Result.map maskIds)
                           >> successOrBadReq ctx (Controller.json ctx)))
    create
      (fun ctx ->
      task
        {
        let! (newPlayer : NewPlayer) = Controller.getJson ctx
        return! showGame gameId
                |> successOr404 ctx
                     (createPlayer newPlayer
                      >> successOrBadReq ctx (Controller.json ctx)) })
    show
      (fun ctx id ->
      showGame gameId
      |> successOr404 ctx
           (getPlayer id >> successOr404 ctx (Controller.json ctx)))
  }

let startController gameId =
  controller
    {
    create
      (fun ctx ->
      showGame gameId
      |> successOr404 ctx
           (startGame >> successOrBadReq ctx (Controller.json ctx))) }

let gameController =
  controller {
    subController "/start" startController
    subController "/players" playerController
    index (fun ctx -> getActiveGames() |> Controller.json ctx)
    create (fun ctx ->
      task {
        let! (ng : NewGame) = Controller.getJson ctx
        return! ng
                |> createGame
                |> Controller.json ctx
      })
    show
      (fun ctx (id : string) ->
      showGame id |> resultTo404 ctx "unable to find game")
  }

let webApp =
  router {
    forward "/api/games" gameController
    get "/api/init" (fun next ctx -> task { let! counter = getInitCounter()
                                            return! json counter next ctx })
    get "/api/ping" (fun next ctx ->
      task {
        let hub = ctx.GetService<Channels.ISocketHub>()
        hub.SendMessageToClients "/ws" "hello" "world" |> ignore
        return! json "success!" next ctx
      })
  }
