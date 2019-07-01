module Api

open System.Threading.Tasks
open FSharp.Control.Tasks.V2
open Giraffe
open Saturn
open StackExchange.Redis
open Shared

let redis = ConnectionMultiplexer.Connect("localhost")
let db = redis.GetDatabase()
let getInitCounter() : Task<Counter> = task { return { Value = 42 } }

let gameController =
  controller {
    index (fun ctx -> "hello world" |> Controller.json ctx)
    show (fun ctx id -> sprintf "Hello %s" id |> Controller.json ctx)
  }

let webApp =
  router {
    forward "/api/game" gameController
    get "/api/init" (fun next ctx -> task { let! counter = getInitCounter()
                                            return! json counter next ctx })
  }
