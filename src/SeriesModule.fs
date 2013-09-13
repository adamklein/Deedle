﻿namespace FSharp.DataFrame

/// Series module comment..
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Series = 
  open System.Linq
  open FSharp.DataFrame.Internal
  open FSharp.DataFrame.Vectors
  open MathNet.Numerics.Statistics

  [<CompiledName("Sum")>]
  let inline sum (series:Series<_, _>) = 
    match series.Vector.Data with
    | VectorData.DenseList list -> IReadOnlyList.sum list
    | VectorData.SparseList list -> IReadOnlyList.sumOptional list
    | VectorData.Sequence seq -> Seq.sum (Seq.choose OptionalValue.asOption seq)

  [<CompiledName("Mean")>]
  let inline mean (series:Series<_, _>) = 
    match series.Vector.Data with
    | VectorData.DenseList list -> IReadOnlyList.average list
    | VectorData.SparseList list -> IReadOnlyList.averageOptional list
    | VectorData.Sequence seq -> Seq.average (Seq.choose OptionalValue.asOption seq)

  // Non-public helper
  let inline private streamingAggregation f (series:Series<_, float>) =
    match series.Vector.Data with
    | VectorData.DenseList list -> f (list :> seq<_>)
    | VectorData.SparseList list -> f (Seq.choose OptionalValue.asOption list)
    | VectorData.Sequence seq -> f (Seq.choose OptionalValue.asOption seq)


  /// If there are missing values, they are skipped over.
  [<CompiledName("StandardDeviation")>]
  let inline sdv (series:Series<'K, float>) = series |> streamingAggregation Statistics.StandardDeviation
  [<CompiledName("Median")>]
  let inline median (series:Series<'K, float>) = series |> streamingAggregation Statistics.Median


  let observations (series:Series<'K, 'T>) = series.Observations

  /// Create a new series that contains values for all provided keys.
  /// Use the specified lookup semantics - for exact matching, use `getAll`
  let lookupAll keys lookup (series:Series<'K, 'T>) = series.GetItems(keys, lookup)

  /// Create a new series that contains values for all provided keys.
  /// Uses exact lookup semantics for key lookup - use `lookupAll` for more options
  let getAll keys (series:Series<'K, 'T>) = series.GetItems(keys)

  /// Get the value for the specified key.
  /// Use the specified lookup semantics - for exact matching, use `get`
  let lookup key lookup (series:Series<'K, 'T>) = series.Get(key, lookup)

  /// Get the value for the specified key.
  /// Uses exact lookup semantics for key lookup - use `lookupAll` for more options
  let get key (series:Series<'K, 'T>) = series.Get(key)

  let withOrdinalIndex (series:Series<'K, 'T>) = 
    series.WithOrdinalIndex()

  let filter f (series:Series<'K, 'T>) = 
    series.Where(fun kvp -> f kvp.Key kvp.Value)

  let map (f:'K -> 'T -> 'R) (series:Series<'K, 'T>) = 
    series.Select(fun kvp -> f kvp.Key kvp.Value)

  let mapValues (f:'T -> 'R) (series:Series<'K, 'T>) = 
    series.Select(fun kvp -> f kvp.Value)

  let mapKeys (f:'K -> 'R) (series:Series<'K, 'T>) = 
    series.SelectKeys(fun kvp -> f kvp.Key)

  let filterAll f (series:Series<'K, 'T>) = 
    series.WhereOptional(fun kvp -> f kvp.Key (OptionalValue.asOption kvp.Value))

  let mapAll (f:_ -> _ -> option<'R>) (series:Series<'K, 'T>) = 
    series.SelectOptional(fun kvp -> 
      f kvp.Key (OptionalValue.asOption kvp.Value) |> OptionalValue.ofOption)

  let pairwise (series:Series<'K, 'T>) = 
    series.Pairwise() |> map (fun k v -> v.Data)
  
  let pairwiseWith f (series:Series<'K, 'T>) = 
    series.Pairwise() |> map (fun k v -> f k v.Data)

  let inline diff offset (series:Series<'K, 'T>) = 
    series.Aggregate
      ( WindowSize((abs offset) + 1, Boundary.Skip), 
        (fun ds -> let h, t = ds.Data.Values.First(), ds.Data.Values.Last() in t - h),
        (fun ks -> if offset > 0 then ks.Data.Keys.First() else ks.Data.Keys.Last() ) )

  (**
  Windowing, Chunking and Grouping
  ----------------------------------------------------------------------------------------------

  The functions with name starting with `windowed` take a series and generate floating 
  (overlapping) windows. The `chunk` functions 

  *)

  let aggregate aggregation keySelector (series:Series<'K, 'T>) : Series<'TNewKey, _> =
    series.Aggregate
      ( aggregation, System.Func<_, _>(id), System.Func<_, _>(keySelector))

  let aggregateInto aggregation keySelector valueSelector (series:Series<'K, 'T>) : Series<'TNewKey, 'R> =
    series.Aggregate
      ( aggregation, System.Func<_, _>(valueSelector), System.Func<_, _>(keySelector))

  // Window based on size

  let windowSizeInto bounds f (series:Series<'K, 'T>) : Series<'K, 'R> =
    let dir = if snd bounds = Boundary.AtEnding then Direction.Forward else Direction.Backward
    let keySel = System.Func<DataSegment<Series<_, _>>, _>(fun data -> 
      if dir = Direction.Backward then data.Data.Index.Keys |> Seq.last
      else data.Data.Index.Keys |> Seq.head )
    series.Aggregate(Aggregation.WindowSize(bounds), (fun ds -> f ds), keySel)

  let inline windowSize bounds (series:Series<'K, 'T>) = 
    windowSizeInto bounds DataSegment.data series 

  // Based on distance

  let inline windowDistInto distance f (series:Series<'K, 'T>) =
    series.Aggregate(Aggregation.WindowWhile(fun skey ekey -> (ekey - skey) < distance), fun ds -> f ds.Data)
  let inline windowDist distance (series:Series<'K, 'T>) = 
    windowDistInto distance id series 

  // Window using while

  let inline windowWhileInto cond f (series:Series<'K, 'T>) =
    series.Aggregate(Aggregation.WindowWhile(cond), fun ds -> f ds.Data)
  let inline windowWhile cond (series:Series<'K, 'T>) = 
    windowWhileInto cond id series 

  // Chunk based on size

  let inline chunkSizeInto bounds f (series:Series<'K, 'T>) : Series<'K, 'R> =
    series.Aggregate(Aggregation.ChunkSize(bounds), fun ds -> f ds)
  let inline chunkSize bounds (series:Series<'K, 'T>) = 
    chunkSizeInto bounds DataSegment.data series 

  // Chunk based on distance

  let inline chunkDistInto (distance:^D) f (series:Series<'K, 'T>) : Series<'K, 'R> =
    series.Aggregate(Aggregation.ChunkWhile(fun skey ekey -> (ekey - skey) < distance), fun ds -> f ds.Data)
  let inline chunkDist (distance:^D) (series:Series<'K, 'T>) = 
    chunkDistInto distance id series 

  // Chunk while

  let inline chunkWhileInto cond f (series:Series<'K, 'T>) =
    series.Aggregate(Aggregation.ChunkWhile(cond), fun ds -> OptionalValue(f ds.Data))
  let inline chunkWhile cond (series:Series<'K, 'T>) = 
    chunkWhileInto cond id series 

  // Skipping most-common case functions

  let inline windowInto size f (series:Series<'K, 'T>) : Series<'K, 'R> =
    windowSizeInto (size, Boundary.Skip) (DataSegment.data >> f) series
  let inline window size (series:Series<'K, 'T>) =
    windowSize (size, Boundary.Skip) series

  let inline chunkInto size f (series:Series<'K, 'T>) : Series<'K, 'R> =
    chunkSizeInto (size, Boundary.Skip) (DataSegment.data >> f) series
  let inline chunk size (series:Series<'K, 'T>) =
    chunkSize (size, Boundary.Skip) series

  // Grouping

  let groupInto (keySelector:'K -> 'T -> 'TNewKey) f (series:Series<'K, 'T>) : Series<'TNewKey, 'TNewValue> =
    series.GroupBy(keySelector, fun k s -> OptionalValue(f k s))

  let groupBy (keySelector:'K -> 'T -> 'TNewKey) (series:Series<'K, 'T>) =
    groupInto keySelector (fun k s -> s) series

  // ----------------------------------------------------------------------------------------------
  // Counting & checking if values are present
  // ----------------------------------------------------------------------------------------------

  let countValues (series:Series<'K, 'T>) = series.CountValues
  let countKeys (series:Series<'K, 'T>) = series.CountKeys

  let hasAll keys (series:Series<'K, 'T>) = 
    keys |> Seq.forall (fun k -> series.TryGet(k).IsSome)
  let hasSome keys (series:Series<'K, 'T>) = 
    keys |> Seq.exists (fun k -> series.TryGet(k).IsSome)
  let hasNone keys (series:Series<'K, 'T>) = 
    keys |> Seq.forall (fun k -> series.TryGet(k).IsNone)
  let has key (series:Series<'K, 'T>) = series.TryGet(key).IsSome
  let hasNot key (series:Series<'K, 'T>) = series.TryGet(key).IsNone

  // ----------------------------------------------------------------------------------------------
  // Handling of missing values
  // ----------------------------------------------------------------------------------------------

  let dropMissing (series:Series<'K, 'T>) = series.DropMissing()

  let fillMissingUsing f (series:Series<'K, 'T>) = 
    series |> mapAll (fun k -> function 
      | None -> Some(f k)
      | value -> value)

  let fillMissingWith value (series:Series<'K, 'T>) = 
    series |> mapAll (fun k -> function 
      | None -> Some(value)
      | value -> value)

  let fillMissing direction (series:Series<'K, 'T>) = 
    let lookup = if direction = Direction.Forward then Lookup.NearestSmaller else Lookup.NearestGreater
    series |> mapAll (fun k -> function 
      | None -> series.TryGet(k, lookup)
      | value -> value)

  let values (series:Series<'K, 'T>) = series.Values
  let keys (series:Series<'K, 'T>) = series.Keys


  // TODO: This can be simplified using fancier aggregate?

  let shift offset (series:Series<'K, 'T>) = 
    let shifted = 
      if offset < 0 then
        let offset = -offset
        series |> aggregateInto (WindowSize(offset + 1, Boundary.Skip)) 
          (fun s -> s.Data.Keys.First())
          (fun s -> s.Data.Values |> Seq.nth offset)          
      else
        series |> aggregateInto (WindowSize(offset + 1, Boundary.Skip)) 
          (fun s -> s.Data.Keys.Last())
          (fun s -> s.Data.Values |> Seq.head)           
    shifted.GetItems(series.Keys)

  let takeLast count (series:Series<'K, 'T>) = 
    let keys = series.Keys |> Seq.lastFew count 
    Series(keys, seq { for k in keys -> series.[k] })