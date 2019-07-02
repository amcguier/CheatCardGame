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

type NewGame =
  { Username : string
    Players : int }

type Game =
  { GameId : string
    Owner : string
    Players : int
    PlayersConnected : int }

type Counter =
  { Value : int }
