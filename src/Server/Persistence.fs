module Persistence

open System
open StackExchange.Redis
open Shared
open Thoth.Json.Net
open System.Collections.Generic

let redisConfig =
  Environment.GetEnvironmentVariable "REDIS_SERVER"
  |> function
  | null
  | "" -> "localhost:6379"
  | s -> s

let redis = ConnectionMultiplexer.Connect(redisConfig)
let db = redis.GetDatabase()
let inline (~~) (x : ^a) : ^b =
  ((^a or ^b) : (static member op_Implicit : ^a -> ^b) x)
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

let storeExpiringSet (key : String) (entry : String) =
  let ky : RedisKey = ~~key
  let element : RedisValue = ~~entry
  let score = (DateTimeOffset.UtcNow.ToUnixTimeSeconds() |> float)
  let entry = SortedSetEntry(element, score)
  db.SortedSetAdd(ky, [| entry |]) |> ignore

let expireSet (key : String) (timeStamp : int64) =
  let ky : RedisKey = ~~key
  let score = timeStamp |> float
  let startScore = 0.0
  db.SortedSetRemoveRangeByScore(ky, startScore, score)

let gamesListKey = "games"

let getActiveGames() =
  let timeStamp =
    DateTimeOffset.UtcNow.Subtract(gameTimeout).ToUnixTimeSeconds()
  expireSet gamesListKey timeStamp |> ignore
  let ky : RedisKey = ~~gamesListKey
  db.SortedSetRangeByRank(ky, 0L, Int64.MaxValue)
  |> Array.map ((fun x ->
                let (s : string) = ~~x
                s)
                >> (fun (s : string) -> s.Split(':'))
                >> (fun arr ->
                { Id = arr.[0]
                  OwnerName = arr.[1] }))
  |> Array.toList

let createGame (newGame : NewGame) =
  let id = Guid.NewGuid().ToString()

  let game =
    { GameId = id
      Owner = newGame.Username
      Players = newGame.Players
      PlayersConnected = 0
      IsStarted = false
      IsFinished = false }
  Some(gameTimeout)
  |> storeRedis (gameKey id) game
  |> ignore
  let gameInfo = sprintf "%s:%s" game.GameId game.Owner
  storeExpiringSet gamesListKey gameInfo
  game

let playersSetKey gameId = (gameKey gameId) + ":players"
let playersByUserKey gameId = (gameKey gameId) + ":players:userids"
let playersCount gameId = playersSetKey gameId + ":count"

let getPlayerCount gameId : int =
  let id : RedisKey = ~~(playersCount gameId)
  ~~db.StringGet(id)
  |> function
  | null -> 0
  | s -> Int32.Parse s

let showGame (gameId : string) : Result<Game, String> =
  let gameKey : RedisKey = ~~(gameKey gameId)
  ~~(db.StringGet gameKey)
  |> function
  | null -> Error("No value from redis")
  | s -> Ok(s)
  |> Result.bind Decode.Auto.fromString<Game>
  |> Result.map
       (fun game -> { game with PlayersConnected = getPlayerCount gameId })
  |> Result.mapError (fun _ -> "No such game")

let playerKey gameId playerId =
  sprintf "%s:player:%s" (playersSetKey gameId) playerId

let storePlayer game (player : Player) =
  let ky : RedisKey = ~~(playersSetKey game.GameId)
  let playerKey : RedisValue = ~~(player.Id)
  let value : RedisValue = ~~(Encode.Auto.toString (2, player, extra = extra))
  db.HashSet(ky, [| HashEntry(playerKey, value) |])
  db.KeyExpire(ky, Nullable(gameTimeout)) |> ignore

let maskIds (players : Player list) =
  List.map (fun (player : Player) -> { player with Id = "" }) players

let getPlayers game =
  let accumResult resultList playerResult =
    match (resultList, playerResult) with
    | (Ok(lst), Ok(player)) -> Ok(player :: lst)
    | (Error(s), _) -> Error(s)
    | (_, Error(s)) -> Error("Not all players are valid")

  let ky : RedisKey = ~~(playersSetKey game.GameId)
  db.HashGetAll(ky)
  |> Array.map
       ((fun hashSet -> ~~hashSet.Value)
        >> (fun playerStr ->
        Decode.Auto.fromString<Player> (playerStr, extra = extra)))
  |> Array.fold accumResult (Ok([]))

let getPlayer (pid : string) (game : Game) =
  let ky : RedisKey = ~~(playersSetKey game.GameId)
  let playerKey : RedisValue = ~~pid
  ~~db.HashGet(ky, playerKey)
  |> function
  | null -> Error("No such player")
  | s ->
    Decode.Auto.fromString<Player> (s, extra = extra)
    |> Result.mapError
         (fun _ -> "Couldn't deserialize that player, the game is corrupt")

let createPlayer (np : NewPlayer) (game : Game) =
  let userKey : RedisKey = ~~(playersByUserKey game.GameId)
  let usr : RedisValue = ~~(np.Username)
  let countKey : RedisKey = ~~(playersCount game.GameId)

  let storePlayer np position : Player =
    let player =
      { Username = np.Username
        Position = position
        Dealer = position = 1L
        Hand = None
        Id = Guid.NewGuid().ToString() }
    storePlayer game player
    let uredis : RedisValue = ~~np.Username
    db.SetAdd(userKey, uredis) |> ignore
    db.KeyExpire(userKey, Nullable(gameTimeout)) |> ignore
    player
  if db.SetContains(userKey, usr) then Error("Player already exists")
  else
    Ok "NextStep"
    |> Result.map (fun _ -> db.StringIncrement(countKey))
    |> Result.map (storePlayer np)

let turnKey gameId = (gameKey gameId) + ":turns"
let playKey gameId = (gameKey gameId) + ":plays"

let emptyTurn =
  { Id = Guid.NewGuid().ToString()
    CardValue = Ace
    CardsDown = None
    Position = 2L
    PlaysMade = 0
    TurnOver = false }

type Play =
  | Pass
  | Call
  | Cards of Card list

let getCurrentTurn (game : Game) =
  let turnKey : RedisKey = ~~(turnKey game.GameId)
  if not game.IsStarted then Error("The game hasn't started")
  else
    (~~db.ListGetByIndex(turnKey, 0L))
    |> Decode.Auto.fromString<Turn>
    |> Result.mapError (fun _ -> "No turns found, try a different game")

type GameData = Game * Player * Turn * Play

let validateCards (player : Player) (cards : Card list) onSuccess =
  let hand = Set.ofList (Option.defaultValue [] player.Hand)
  let cardSet = Set.ofList cards
  Set.intersect hand cardSet
  |> Set.count
  |> fun ct ->
    if cards.Length = ct then Ok(onSuccess)
    else Error("You must provide cards in your hand")

let validatePlay (data : GameData) =
  match data with
  | (g, _, _, _) when g.IsFinished ->
    Error("Game is finished, no play is possible")
  | (g, _, t, _) when t.TurnOver ->
    Error("Turn is finished, no play is possible")
  | (_, p, t, Play.Cards(cards)) when p.Position = t.Position ->
    validateCards p cards data
  | (_, p, t, Pass) when p.Position <> t.Position -> Ok(data)
  | (_, p, t, Call) when p.Position <> t.Position && t.CardsDown.IsSome ->
    Ok(data)
  | _ -> Error("Invalid play information provided")

let makePlay gameId playerId play =
  let playData =
    showGame gameId
    |> Result.bind
         (fun game -> getPlayer playerId game |> Result.map (fun p -> (game, p)))
    |> Result.bind
         (fun (g, p) ->
         getCurrentTurn g |> Result.map (fun t -> (g, p, t, play)))
  playData |> Result.map (fun (_, _, t, _) -> t)

let initializeTurnList gameId =
  let turnKey : RedisKey = ~~(turnKey gameId)
  let turn : RedisValue = ~~(Encode.Auto.toString (2, emptyTurn, extra = extra))
  db.ListLeftPush(turnKey, [| turn |]) |> ignore
  db.KeyExpire(turnKey, Nullable(gameTimeout)) |> ignore

let setupInitialPlayerHands game (hands : Hand list) =
  let players =
    getPlayers game
    |> function
    | Ok(players) -> players
    | _ -> failwith "Invalid state when starting game"
    |> List.sortBy (fun p -> p.Position)

  let sortedHands =
    hands
    |> List.sortByDescending (fun h -> h.Length)
    |> function
    | (a :: rem) -> rem @ [ a ]
    | l -> l

  List.zip players sortedHands |> List.iter (function
                                    | (player, hand) ->
                                      let uplayer =
                                        { player with Hand = hand |> Some }
                                      storePlayer game uplayer)

let startGame (game : Game) : Result<Game, string> =
  let pc = getPlayerCount game.GameId
  match (pc, game.IsStarted) with
  | (_, true) -> Error("Game is already started")
  | (x, _) when x <> game.Players -> Error("Not all players connected")
  | _ ->
    initializeTurnList game.GameId
    let updatedGame =
      { game with IsStarted = true
                  PlayersConnected = pc }
    storeRedis (gameKey updatedGame.GameId) updatedGame (Some(gameTimeout))
    |> ignore
    CardUtilities.createDeck()
    |> CardUtilities.deal updatedGame.Players
    |> setupInitialPlayerHands updatedGame
    // Call the method to deal to the players
    updatedGame |> Ok
