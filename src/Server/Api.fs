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
let gameKey = sprintf "game:%s"

let storeRedis (key : string) value (timeSpan : TimeSpan Option) =
  let ky : RedisKey = ~~key
  let vl : RedisValue = ~~(Encode.Auto.toString (2, value))
  if timeSpan.IsSome then
    db.StringSet
      (ky, vl,
       Option.defaultWith (fun () -> TimeSpan.FromMinutes 0.0) timeSpan
       |> Nullable)
  else db.StringSet(ky, vl)

let createGame (newGame : NewGame) =
  let id = Guid.NewGuid().ToString()

  let game =
    { GameId = id
      Owner = newGame.Username
      Players = newGame.Players
      PlayersConnected = 0
      IsStarted = false }
  TimeSpan.FromMinutes 15.0
  |> Some
  |> storeRedis (gameKey id) game
  |> ignore
  game

let showGame (gameId : string) =
  let gameKey : RedisKey = ~~(gameKey gameId)
  ~~(db.StringGet gameKey)
  |> function
  | null -> Error("No value from redis")
  | s -> Ok(s)
  |> Result.bind Decode.Auto.fromString<Game>
  |> function
  | Ok(game) -> Some(game)
  | Error(str) -> None

let gameController =
  controller {
    index (fun ctx -> "hello world" |> Controller.json ctx)
    create (fun ctx ->
      task {
        let! (ng : NewGame) = Controller.getJson ctx
        return ng
               |> createGame
               |> Controller.json ctx
      })
    show (fun ctx (id : string) ->
      showGame id
      |> function
      | Some(game) -> Controller.json ctx game
      | None -> (Response.notFound ctx "Game Not Found"))
  }

let webApp =
  router {
    forward "/api/games" gameController
    get "/api/init" (fun next ctx -> task { let! counter = getInitCounter()
                                            return! json counter next ctx })
  }
