namespace Shared

type Suit =
  | Clubs
  | Spades
  | Hearts
  | Diamonds

type Value =
  | Number of int
  | Jack
  | Queen
  | King
  | Ace

type Card =
  { Suit : Suit
    Value : Value }

type Hand = Card List

type GameState =
  { Hand : Hand
    PlayerCounts : int list
    DiscardCount : int }

type GameInfo =
  { Id : string
    OwnerName : string }

type NewGame =
  { Username : string
    Players : int }

type Player =
  { Username : string
    Position : int64
    Dealer : bool
    Hand : Hand option
    Id : string }

type NewPlayer =
  { Username : string }

type Game =
  { GameId : string
    Owner : string
    Players : int
    PlayersConnected : int
    IsStarted : bool
    IsFinished : bool }

type Counter =
  { Value : int }

type Turn =
  { Id : string
    CardValue : Value
    Position : int64
    PlaysMade : int
    CardsDown : int option
    TurnOver : bool }

type Play =
  | Pass
  | Call
  | Cards of Card list

type PlayRecord =
  { TurnId : string
    PlayerId : string
    Play : Play }
