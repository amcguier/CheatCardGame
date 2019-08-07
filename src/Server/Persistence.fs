module Persistence

open System
open StackExchange.Redis
open Shared
open Thoth.Json.Net
open System.Collections.Generic
open FSharp.Control.Tasks.V2
open System.Threading.Tasks

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
let subscriber = redis.GetSubscriber()
let unsubscribeMessage = "UNSUBSCRIBE"

let subscribeToSocket (gameId : Guid) (broadCastAction : string -> unit) =
  let channel : RedisChannel = ~~(gameId.ToString())

  let subscription (msg : ChannelMessage) =
    let strMsg : string = ~~(msg.Message)
    if strMsg = unsubscribeMessage then subscriber.Unsubscribe(msg.Channel)
    else strMsg |> broadCastAction
  subscriber.Subscribe(channel).OnMessage(subscription)

let unsubscribeGameSockets (gameId : Guid) =
  let channel : RedisChannel = ~~(gameId.ToString())
  subscriber.Unsubscribe(channel)

let publishGameSocket (gameId : string) (message : string) =
  let channel : RedisChannel = ~~(gameId)
  let msg : RedisValue = ~~message
  subscriber.Publish(channel, msg) |> ignore

let createAndSerializeSocketMessage topic (mp : MessagePayload) =
  { Topic = topic
    Payload = mp }
  |> fun ob -> Encode.Auto.toString (2, ob, extra = extra)

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
  let arrayToGameObject (arr : string array) =
    { Id = arr.[0]
      OwnerName = arr.[1] }

  let timeStamp =
    DateTimeOffset.UtcNow.Subtract(gameTimeout).ToUnixTimeSeconds()
  expireSet gamesListKey timeStamp |> ignore
  let ky : RedisKey = ~~gamesListKey
  db.SortedSetRangeByRank(ky, 0L, Int64.MaxValue)
  |> Array.map ((fun x ->
                let (s : string) = ~~x
                s)
                >> (fun (s : string) -> s.Split(':'))
                >> arrayToGameObject)
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
  |> Result.bind (fun s -> Decode.Auto.fromString<Game> (s, extra = extra))
  |> Result.map
       (fun game -> { game with PlayersConnected = getPlayerCount gameId })
  |> Result.mapError (fun _ -> "No such game")

let playerKey gameId playerId =
  sprintf "%s:player:%s" (playersSetKey gameId) playerId

let storePlayer (game : Game) (player : Player) =
  let ky : RedisKey = ~~(playersSetKey game.GameId)
  let playerKey : RedisValue = ~~(player.Id)
  let value : RedisValue = ~~(Encode.Auto.toString (2, player, extra = extra))
  db.HashSet(ky, [| HashEntry(playerKey, value) |])
  db.KeyExpire(ky, Nullable(gameTimeout)) |> ignore

let maskIds (players : Player list) =
  List.map (fun (player : Player) -> { player with Id = "" }) players

let getPlayers (game : Game) =
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
    let player : Player =
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
let pileKey gameId : RedisKey = ~~((gameKey gameId) + ":pile")

let mergeResult =
  function
  | (Ok(o), Ok(rem)) -> Ok(o :: rem)
  | (Error(s), _) -> Error(s)
  | (_, Error(s)) -> Error(s)

let getAndEmptyPile (game : Game) =
  let key = pileKey game.GameId

  let pile =
    db.ListRange(key, 0L, Int64.MaxValue)
    |> Array.map
         ((~~)
          >> (fun s -> Decode.Auto.fromString<Card list> (s, extra = extra)))
    |> fun arr -> Array.foldBack (fun o s -> mergeResult (o, s)) arr (Ok([]))
  db.KeyDelete(key) |> ignore
  pile

let addToPile (game : Game) (cardsToAdd) =
  let cards : RedisValue =
    ~~(Encode.Auto.toString (2, cardsToAdd, extra = extra))
  db.ListLeftPush(pileKey game.GameId, cards) |> ignore

let emptyTurn =
  { Id = Guid.NewGuid().ToString()
    CardValue = Ace
    CardsDown = None
    Position = 2L
    PlaysMade = 0
    TurnOver = false }

let getCurrentTurn (game : Game) =
  let turnKey : RedisKey = ~~(turnKey game.GameId)
  if not game.IsStarted then Error("The game hasn't started")
  else
    (~~db.ListGetByIndex(turnKey, 0L))
    |> fun s -> Decode.Auto.fromString<Turn> (s, extra = extra)
    |> Result.mapError (fun _ -> "No turns found, try a different game")

type GameData =
  { Game : Game
    Player : Player
    Turn : Turn
    Play : Play }

let validateCards (player : Player) (cards : Card list) onSuccess =
  let hand = Set.ofList (Option.defaultValue [] player.Hand)
  let cardSet = Set.ofList cards
  Set.intersect hand cardSet
  |> Set.count
  |> fun ct ->
    if cards.Length = ct then Ok(onSuccess)
    else Error("You must provide cards in your hand")

let validatePlay (data : GameData) =
  match (data, data.Play) with
  | (d, _) when d.Game.IsFinished ->
    Error("Game is finished, no play is possible")
  | (d, _) when d.Turn.TurnOver ->
    Error("Turn is finished, no play is possible")
  | (d, Play.Cards(cards)) when d.Player.Position = d.Turn.Position ->
    validateCards d.Player cards data
  | (d, Pass) when d.Player.Position <> d.Turn.Position -> Ok(data)
  | (d, Call) when d.Player.Position <> d.Turn.Position
                   && d.Turn.CardsDown.IsSome -> Ok(data)
  | _ -> Error("Invalid play information provided")

let lastPlayWasTrue (lst : Card list) (turn : Turn) =
  List.forall (fun (card : Card) -> card.Value = turn.CardValue) lst

let handleCallCards (data : GameData) (pile : Card list list) =
  let lastPlay = List.head pile
  let newCards = List.collect id pile
  let playTrue = lastPlayWasTrue lastPlay data.Turn

  let playerToUpdate =
    if playTrue then data.Player
    else
      (getPlayers data.Game)
      |> function
      | Ok(players) ->
        List.find (fun p -> p.Position = data.Turn.Position) players
      | _ -> failwith "Invalid player state during game"
  { CallPosition = data.Player.Position
    Cards = lastPlay
    WasLie = not playTrue }
  |> CallMade
  |> createAndSerializeSocketMessage "CALLED"
  |> publishGameSocket data.Game.GameId
  let newHand = playerToUpdate.Hand.Value @ newCards |> Some
  storePlayer data.Game { playerToUpdate with Hand = newHand }
  { data.Turn with TurnOver = true }

//load current position and add that players cards
let executeCall (data : GameData) =
  // Test if last entry on the pile are all members of the turn value
  // if yes, pile goes to calling player, if not, pile does to position player
  getAndEmptyPile data.Game
  |> Result.map (handleCallCards data)
  |> function
  | Ok(turn) -> turn
  | Error(_) -> failwith "Invalid internal state during play"

let executePlayCards (data : GameData) cards =
  addToPile data.Game cards
  let newHand = List.except cards data.Player.Hand.Value |> Some
  let newPlayer = { data.Player with Hand = newHand }
  storePlayer data.Game newPlayer
  createAndSerializeSocketMessage "CARDS_PLAYED" NoPayload
  |> publishGameSocket (data.Game.GameId)
  { data.Turn with CardsDown =
                     (cards
                      |> List.length
                      |> Some) }

// TODO notify other players this has happened
let storeUpdatedTurn (game : Game) (turn : Turn) =
  let turnKey : RedisKey = ~~(turnKey game.GameId)

  let updatedTurn =
    if turn.PlaysMade = game.Players then { turn with TurnOver = true }
    else turn

  let serialized : RedisValue =
    ~~(Encode.Auto.toString (2, updatedTurn, extra = extra))
  db.ListLeftPop(turnKey) |> ignore
  db.ListLeftPush(turnKey, serialized) |> ignore
  if updatedTurn.TurnOver then
    let newTurn =
      { Id = Guid.NewGuid().ToString()
        CardValue = CardUtilities.nextValue updatedTurn.CardValue
        Position =
          if turn.Position = int64 (game.Players) then 1L
          else turn.Position + 1L
        CardsDown = None
        PlaysMade = 0
        TurnOver = false }

    let newSerialized : RedisValue =
      ~~(Encode.Auto.toString (2, newTurn, extra = extra))
    db.ListLeftPush(turnKey, newSerialized) |> ignore
  updatedTurn

let handleTurnover (game : Game) (turn : Turn) =
  if turn.TurnOver then
    createAndSerializeSocketMessage "TURN_OVER" (TurnOver turn.Id)
    |> publishGameSocket game.GameId
  turn

let handleGameOver (game : Game) (turn : Turn) =
  //We only need to check game over if the turn is over
  if turn.TurnOver then
    let players =
      getPlayers game
      |> function
      | Ok(players) -> players
      | Error(e) -> failwith e

    let emptyPlayers =
      players
      |> List.filter (fun p -> p.Hand.IsSome && p.Hand.Value |> List.isEmpty)
    if emptyPlayers.Length > 0 then
      let winner = emptyPlayers.Head
      { // send socket message
        GameId = game.GameId
        WinningPosition = winner.Position }
      |> GameOver
      |> createAndSerializeSocketMessage "GAME_OVER"
      |> publishGameSocket game.GameId
      { // update game value
        game with IsFinished = true }
      |> fun g -> storeRedis (gameKey game.GameId) g (Some(gameTimeout))
      |> ignore
      // unsubscribe all socket players
      publishGameSocket game.GameId unsubscribeMessage
  // unsubscribe our subscribers
  turn

//TODO notify that the turn is over
let executeValidatedPlay (data : GameData) =
  match data.Play with
  | Pass -> data.Turn // do nothing
  | Call -> executeCall data
  | Cards(cards) -> executePlayCards data cards
  |> fun t -> { t with PlaysMade = t.PlaysMade + 1 }
  |> storeUpdatedTurn data.Game
  |> handleTurnover data.Game
  |> handleGameOver data.Game
  |> fun turn -> { data with Turn = turn }

let storePlay (data : GameData) =
  let key : RedisKey = ~~(playKey data.Game.GameId)

  let record =
    { TurnId = data.Turn.Id
      PlayerId = data.Player.Id
      Play = data.Play }

  let value : RedisValue = ~~(Encode.Auto.toString (2, record, extra = extra))
  db.ListLeftPush(key, value) |> ignore
  data

let makePlay gameId playerId (play : Play) =
  let playData =
    showGame gameId
    |> Result.bind
         (fun game -> getPlayer playerId game |> Result.map (fun p -> (game, p)))
    |> Result.bind (fun (g, p) ->
         getCurrentTurn g
         |> Result.map (fun t ->
              { Game = g
                Player = p
                Turn = t
                Play = play }))
  playData
  |> Result.bind validatePlay
  |> Result.map (executeValidatedPlay
                 >> storePlay
                 >> (fun data -> data.Turn))

let initializeTurnList gameId =
  let turnKey : RedisKey = ~~(turnKey gameId)
  let turn : RedisValue = ~~(Encode.Auto.toString (2, emptyTurn, extra = extra))
  db.ListLeftPush(turnKey, [| turn |]) |> ignore
  db.KeyExpire(turnKey, Nullable(gameTimeout)) |> ignore

let setupInitialPlayerHands (game : Game) (hands : Hand list) =
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
    { // Call the method to deal to the players
      Topic = "GAME_STARTED"
      Payload = NoPayload }
    |> fun obj -> Encode.Auto.toString (2, obj, extra = extra)
    |> publishGameSocket (updatedGame.GameId)
    updatedGame |> Ok
