module CardUtilities

open Shared
open System
open System.Security.Cryptography

let rand = Random()
let r = new RNGCryptoServiceProvider()
let suits = [ Clubs; Spades; Hearts; Diamonds ]
let faces = [ Jack; Queen; King; Ace ]

let createDeck() : Hand =
  let numeric =
    seq {
      for i in 2..10 do
        let nm = Number(i)
        for s in suits do
          yield { Suit = s
                  Value = nm }
    }

  let faces =
    seq {
      for f in faces do
        for s in suits do
          yield { Suit = s
                  Value = f }
    }

  Seq.append numeric faces |> List.ofSeq

let shuffle (hand : Hand) : Hand = hand |> List.sortBy (fun _ -> rand.Next())

let nextValue =
  function
  | Ace -> Number(2)
  | Number(10) -> Jack
  | Jack -> Queen
  | Queen -> King
  | King -> Ace
  | Number(x) -> Number(x + 1)

let deal people hand : Hand list =
  hand
  |> shuffle
  |> List.splitInto people
