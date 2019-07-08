module Api

open System
open System.Threading.Tasks
open FSharp.Control.Tasks.V2
open Giraffe
open Saturn
open StackExchange.Redis
open Shared
open Thoth.Json.Net
open System.Collections.Generic

let redis = ConnectionMultiplexer.Connect("localhost")
let db = redis.GetDatabase()
let inline (~~) (x : ^a) : ^b =
  ((^a or ^b) : (static member op_Implicit : ^a -> ^b) x)
let getInitCounter() : Task<Counter> = task { return { Value = 42 } }
let gameTimeout = TimeSpan.FromMinutes 120.0
let extra = Extra.empty |> Extra.withInt64

let storeRedis (key : string) value (timeSpan : TimeSpan Option) =
  let ky : RedisKey = ~~key
  let vl : RedisValue = ~~(Encode.Auto.toString (2, value, extra = extra))
  if timeSpan.IsSome then
    db.StringSet
      (ky, vl,
       Option.defaultWith (fun () -> TimeSpan.FromMinutes 0.0) timeSpan
       |> Nullable)
  else db.StringSet(ky, vl)

let gameKey = sprintf "game:%s"

let createGame (newGame : NewGame) =
  let id = Guid.NewGuid().ToString()

  let game =
    { GameId = id
      Owner = newGame.Username
      Players = newGame.Players
      PlayersConnected = 0
      IsStarted = false }
  Some(gameTimeout)
  |> storeRedis (gameKey id) game
  |> ignore
  game

let resultTo404 ctx msg =
  function
  | Ok(game) -> Controller.json ctx game
  | Error(_) -> (Response.notFound ctx msg)

let playersSetKey gameId = (gameKey gameId) + ":players"
let playersCount gameId = playersSetKey gameId + ":count"

let getPlayerCount gameId =
  let id : RedisKey = ~~(playersCount gameId)
  ~~db.StringGet(id)
  |> function
  | null -> 0
  | s -> Int32.Parse s

let showGame (gameId : string) =
  let gameKey : RedisKey = ~~(gameKey gameId)
  ~~(db.StringGet gameKey)
  |> function
  | null -> Error("No value from redis")
  | s -> Ok(s)
  |> Result.bind Decode.Auto.fromString<Game>
  |> Result.map
       (fun game -> { game with PlayersConnected = getPlayerCount gameId })

let deal() = db.StringGetRange
let playerKey gameId playerId =
  sprintf "%s:player:%s" (playersSetKey gameId) playerId

let createPlayer (game : Game) (np : NewPlayer) =
  let ky : RedisKey = ~~(playersSetKey game.GameId)
  let usr : RedisValue = ~~(np.Username)
  let countKey : RedisKey = ~~(playersCount game.GameId)

  let storePlayer np position =
    let player =
      { Username = np.Username
        Position = position
        Dealer = position = 1L
        Hand = None
        Id = Guid.NewGuid().ToString() }

    let playerKey = playerKey game.GameId player.Id
    storeRedis playerKey player (Some gameTimeout) |> ignore
    let uredis : RedisValue = ~~np.Username
    db.SetAdd(ky, uredis) |> ignore
    db.KeyExpire(ky, Nullable(gameTimeout)) |> ignore
    player
  if db.SetContains(ky, usr) then Error("Player already exists")
  else
    Ok "NextStep"
    |> Result.map (fun _ -> db.StringIncrement(countKey))
    |> Result.map (storePlayer np)

let startGame (game : Game) =
  if game.PlayersConnected = game.Players then
    Error("Not all players connected")
  else { game with IsStarted = true } |> Ok

let playerController gameId =
  controller {
    create (fun ctx ->
      task {
        let! (newPlayer : NewPlayer) = Controller.getJson ctx
        return showGame gameId
               |> Result.mapError (fun _ -> "Unable to find the game")
               |> Result.bind (fun game -> createPlayer game newPlayer)
               |> function
               | Ok(player) -> Controller.json ctx player
               | Error(str) -> (Response.badRequest ctx str)
      })
  }

let startController gameId =
  controller
    {
    create (fun ctx -> showGame gameId |> resultTo404 ctx "unable to find game") }

let gameController =
  controller {
    subController "/start" startController
    subController "/players" playerController
    index (fun ctx -> "hello world" |> Controller.json ctx)
    create (fun ctx ->
      task {
        let! (ng : NewGame) = Controller.getJson ctx
        return ng
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
  }
