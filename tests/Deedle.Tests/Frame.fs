﻿#if INTERACTIVE
#I "../../bin"
#load "Deedle.fsx"
#r "../../packages/NUnit.2.6.3/lib/nunit.framework.dll"
#r "../../packages/FsCheck.0.9.1.0/lib/net40-Client/FsCheck.dll"
#r "../../packages/FSharp.Data.1.1.10/lib/net40/FSharp.Data.dll"
#load "../Common/FsUnit.fs"
#else
module Deedle.Tests.Frame
#endif

open System
open System.Data
open System.Dynamic
open System.Collections.Generic
open FsUnit
open FsCheck
open NUnit.Framework
open Deedle

// ------------------------------------------------------------------------------------------------
// Input and output (CSV files)
// ------------------------------------------------------------------------------------------------

let msft() = 
  Frame.ReadCsv(__SOURCE_DIRECTORY__ + "/data/MSFT.csv", inferRows=10) 
  |> Frame.indexRowsDate "Date"

let msftNoHeaders() = 
  let noHeaders =
    IO.File.ReadAllLines(__SOURCE_DIRECTORY__ + "/data/MSFT.csv") 
    |> Seq.skip 1 |> String.concat "\n"
  let data = System.Text.Encoding.UTF8.GetBytes(noHeaders)
  Frame.ReadCsv(new IO.MemoryStream(data), false, inferRows=10)

[<Test>]
let ``Can create empty data frame and empty series`` () =
  let f : Frame<int, int> = frame []  
  let s : Series<int, int> = series []
  s.KeyCount |> shouldEqual 0
  f.ColumnCount |> shouldEqual 0
  f.RowCount |> shouldEqual 0

[<Test>]
let ``Can read MSFT data from CSV file`` () =
  let df = msft()
  df.RowKeys |> Seq.length |> shouldEqual 6527
  df.ColumnKeys |> Seq.length |> shouldEqual 7

[<Test>]
let ``Can read MSFT data from CSV file truncated`` () =
  let df = Frame.ReadCsv(__SOURCE_DIRECTORY__ + "/data/MSFT.csv", inferRows=10, maxRows=27) 
  df.RowKeys |> Seq.length |> shouldEqual 27
  df.ColumnKeys |> Seq.length |> shouldEqual 7

[<Test>]
let ``Can read MSFT data from CSV file without header row`` () =
  let df = msftNoHeaders()
  let expected = msft()  
  let actual = df |> Frame.indexColsWith expected.ColumnKeys |> Frame.indexRowsDate "Date"
  actual |> shouldEqual expected 

[<Test>]
let ``Can save MSFT data as CSV file and read it afterwards (with default args)`` () =
  let file = System.IO.Path.GetTempFileName()
  let expected = msft()
  expected.SaveCsv(file)
  let actual = Frame.ReadCsv(file) |> Frame.indexRowsDate "Date"
  actual |> shouldEqual expected

[<Test>]
let ``Can save MSFT data as CSV file and read it afterwards (with custom format)`` () =
  let file = System.IO.Path.GetTempFileName()
  let expected = msft()
  expected.DropSeries("Date")
  expected.SaveCsv(file, keyNames=["Date"], separator=';', culture=System.Globalization.CultureInfo.GetCultureInfo("cs-CZ"))
  let actual = 
    Frame.ReadCsv(file, separators=";", culture="cs-CZ")
    |> Frame.indexRowsDate "Date" |> Frame.dropSeries "Date"
  actual |> shouldEqual expected

[<Test>]
let ``Can create frame from IDataReader``() =
  let dt = new DataTable()
  dt.Columns.Add(new DataColumn("First", typeof<int>))
  dt.Columns.Add(new DataColumn("Second", typeof<DateTimeOffset>))
  for i in 0 .. 10 do 
    dt.Rows.Add [| box i; box (DateTimeOffset(DateTime.Today.AddDays(float i))) |] |> ignore

  let expected = 
    Frame.ofColumns 
      [ "First" =?> Series.ofValues [ 0 .. 10 ] 
        "Second" =?> Series.ofValues [ for i in 0 .. 10 -> DateTimeOffset(DateTime.Today.AddDays(float i)) ] ]
  
  Frame.ReadReader(dt.CreateDataReader())
  |> shouldEqual expected


[<Test>]
let ``Construction of frame from columns respects specified order``() =
  let df = 
    Frame.ofColumns
      [ "Z" => Series.ofValues [ 1 .. 10 ]
        "X" => Series.ofValues [ 1 .. 10 ] ]
  df.ColumnKeys |> List.ofSeq
  |> shouldEqual ["Z"; "X"]

// ------------------------------------------------------------------------------------------------
// Input and output (from records)
// ------------------------------------------------------------------------------------------------

type MSFT = FSharp.Data.CsvProvider<"data/MSFT.csv">
type Price = { Open : decimal; High : decimal; Low : Decimal; Close : decimal }
type Stock = { Date : DateTime; Volume : int; Price : Price }

let typedRows () = 
  let msft = MSFT.Load(__SOURCE_DIRECTORY__ + "/data/MSFT.csv")
  [| for r in msft.Data -> 
      let p = { Open = r.Open; Close = r.Close; High = r.High; Low = r.High }
      { Date = r.Date; Volume = r.Volume; Price = p } |]
let typedPrices () = 
  [| for r in typedRows () -> r.Price |]

[<Test>]  
let ``Can read simple sequence of records`` () =
  let prices = typedPrices ()
  let df = Frame.ofRecords prices
  set df.ColumnKeys |> shouldEqual (set ["Open"; "High"; "Low"; "Close"])
  df |> Frame.countRows |> shouldEqual prices.Length

[<Test>]  
let ``Can expand properties of a simple record sequence`` () =
  let df = frame [ "MSFT" => Series.ofValues (typedRows ()) ]
  let exp1 = df |> Frame.expandAllCols 1 
  exp1.Rows.[10]?``MSFT.Volume`` |> shouldEqual 49370800.0
  set exp1.ColumnKeys |> shouldEqual (set ["MSFT.Date"; "MSFT.Volume"; "MSFT.Price"])
  
  let exp2 = df |> Frame.expandAllCols 2
  exp2.ColumnKeys |> should contain "MSFT.Price.Open"
  exp2.Rows.[10]?``MSFT.Price.Open`` |> shouldEqual 27.87

[<Test>]  
let ``Can expand properties based on runtime type information`` () =
  let df = frame [ "A" => Series.ofValues [box (1,2); box {Open=1.0M; Close=1.0M; High=1.0M; Low=1.0M}] ]
  let dfexp = FrameExtensions.ExpandColumns(df, 10, true)
  set dfexp.ColumnKeys |> shouldEqual (set ["A.Item1"; "A.Item2"; "A.Open"; "A.High"; "A.Low"; "A.Close"])

[<Test>]  
let ``Can expand properties of specified columns`` () =
  let df = frame [ "MSFT" => Series.ofValues (typedRows ()) ]
  let exp = df |> Frame.expandAllCols 1 |> Frame.expandCols ["MSFT.Price"]
  set exp.ColumnKeys |> shouldEqual (set ["MSFT.Price.Close"; "MSFT.Price.High"; "MSFT.Price.Low"; "MSFT.Date"; "MSFT.Volume"; "MSFT.Price.Open"])

[<Test>]
let ``Can expand vector that mixes ExpandoObjects, records and tuples``() =
  let (?<-) (exp:ExpandoObject) k v = (exp :> IDictionary<_, _>).Add(k, v)
  let exp1 = new ExpandoObject()
  exp1?First <- 1
  exp1?Second <- 2 
  exp1?Nested <- { Open = 1.0M; Low = 0.0M; Close = 2.0M; High = 3.0M }
  let exp2 = new ExpandoObject()
  exp2?Second <- "Test"
  exp2?Nested <- { Open = 1.5M; Low = 0.5M; Close = 2.5M; High = 3.5M }
  let df = frame [ "Objects" => Series.ofValues [(exp1, 42); (exp2, 41)] ]  
  let exp1 = df |> Frame.expandAllCols 1
  set exp1.ColumnKeys |> shouldEqual (set ["Objects.Item1"; "Objects.Item2"])
  let exp3 = df |> Frame.expandAllCols 3
  set exp3.ColumnKeys |> shouldEqual
    ( set ["Objects.Item1.First";       "Objects.Item1.Nested.Close";
           "Objects.Item1.Nested.High"; "Objects.Item1.Nested.Low";
           "Objects.Item1.Nested.Open"; "Objects.Item1.Second"; "Objects.Item2"] )

[<Test>]
let ``Can expand vector that contains SeriesBuilder objects``() =
  let sb = SeriesBuilder<string>()
  sb?Test <- 1
  sb?Another <- "hi"
  let df = frame [ "A" => Series.ofValues [sb]]
  let exp1 = df |> Frame.expandAllCols 1 
  set exp1.ColumnKeys |> shouldEqual (set ["A.Another"; "A.Test"])

[<Test>]
let ``Can expand vector that contains Series<string, T> and tuples``() =
  let df = frame [ "A" => Series.ofValues [ series ["First" => box 1; "Second" => box (1, "Test") ] ]]
  let exp = df |> Frame.expandAllCols 1000
  set exp.ColumnKeys |> shouldEqual (set ["A.First"; "A.Second.Item1"; "A.Second.Item2"])

// ------------------------------------------------------------------------------------------------
// From rows/columns
// ------------------------------------------------------------------------------------------------

[<Test>]
let ``Can create frame from 100k of three element tuples (in less than a few seconds)`` () =
  let values =
    [| for d in 0 .. 100 do
        for i in 0 .. 1000 do
          yield DateTime.Today.AddDays(float d), i.ToString(), 1.0 |]
  let df = Frame.ofValues values
  df |> Frame.sum |> Series.sum |> int |> shouldEqual 101101

// ------------------------------------------------------------------------------------------------
// Stack & unstack
// ------------------------------------------------------------------------------------------------

[<Test>]
let slowStack() = 
  let big = frame [ for d in 0 .. 200 -> string d => series [ for i in 0 .. 500 -> string i => 1.0 ] ]
  let stacked = Frame.stack big 
  stacked |> Frame.countRows |> shouldEqual 100701

[<Test>]
let ``Can group 10x5k data frame by row of type string (in less than a few seconds)`` () =
  let big = frame [ for d in 0 .. 10 -> string d => series [ for i in 0 .. 5000 -> string i => string (i % 1000) ] ]
  let grouped = big |> Frame.groupRowsByString "1"
  grouped.Rows.[ ("998","998") ].GetAs<int>("0") |> shouldEqual 998

[<Test>]
let ``Can group 10x5k data frame by row of type string and nest it (in less than a few seconds)`` () =
  let big = frame [ for d in 0 .. 10 -> string d => series [ for i in 0 .. 5000 -> string i => string (i % 1000) ] ]
  let grouped = big |> Frame.groupRowsByString "1" 
  let nest = grouped |> Frame.nest
  nest |> Series.countKeys |> shouldEqual 1000

// ------------------------------------------------------------------------------------------------
// Numerical operators
// ------------------------------------------------------------------------------------------------

[<Test>]
let ``Applying numerical operation to frame does not affect non-numeric series`` () =
  let df = msft() * 2.0
  let actual = df.GetSeries<DateTime>("Date").GetAt(0).Date 
  actual |> shouldEqual (DateTime(2012, 1, 27))
  
[<Test>]
let ``Can perform numerical operation with a scalar on data frames`` () =
  let df = msft() 

  (df * 2.0)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) * 2.0)
  (df / 2.0)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) / 2.0)
  (df + 2.0)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) + 2.0)
  (df - 2.0)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) - 2.0)
  (2.0 * df)?Open.GetAt(66) |> shouldEqual (2.0 * df?Open.GetAt(66))
  (2.0 + df)?Open.GetAt(66) |> shouldEqual (2.0 + df?Open.GetAt(66))
  (2.0 - df)?Open.GetAt(66) |> shouldEqual (2.0 - df?Open.GetAt(66))
  (2.0 / df)?Open.GetAt(66) |> shouldEqual (2.0 / df?Open.GetAt(66))
  
  (df / 2)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) / 2.0)
  (df * 2)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) * 2.0)
  (df + 2)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) + 2.0)
  (df - 2)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) - 2.0)
  (2 * df)?Open.GetAt(66) |> shouldEqual (2.0 * df?Open.GetAt(66))
  (2 + df)?Open.GetAt(66) |> shouldEqual (2.0 + df?Open.GetAt(66))
  (2 - df)?Open.GetAt(66) |> shouldEqual (2.0 - df?Open.GetAt(66))
  (2 / df)?Open.GetAt(66) |> shouldEqual (2.0 / df?Open.GetAt(66))

[<Test>]
let ``Can perform numerical operation with a series on data frames`` () =
  let df = msft() 

  let opens = df?Open
  (df * opens)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) * opens.GetAt(66))
  (df / opens)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) / opens.GetAt(66))
  (df + opens)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) + opens.GetAt(66))
  (df - opens)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) - opens.GetAt(66))
  (opens * df)?Open.GetAt(66) |> shouldEqual (opens.GetAt(66) * df?Open.GetAt(66))
  (opens + df)?Open.GetAt(66) |> shouldEqual (opens.GetAt(66) + df?Open.GetAt(66))
  (opens - df)?Open.GetAt(66) |> shouldEqual (opens.GetAt(66) - df?Open.GetAt(66))
  (opens / df)?Open.GetAt(66) |> shouldEqual (opens.GetAt(66) / df?Open.GetAt(66))
  
  let opens = int $ df?Open 
  (df * opens)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) * float (opens.GetAt(66)))
  (df / opens)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) / float (opens.GetAt(66)))
  (df + opens)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) + float (opens.GetAt(66)))
  (df - opens)?Open.GetAt(66) |> shouldEqual (df?Open.GetAt(66) - float (opens.GetAt(66)))
  (opens * df)?Open.GetAt(66) |> shouldEqual (float (opens.GetAt(66)) * df?Open.GetAt(66))
  (opens + df)?Open.GetAt(66) |> shouldEqual (float (opens.GetAt(66)) + df?Open.GetAt(66))
  (opens - df)?Open.GetAt(66) |> shouldEqual (float (opens.GetAt(66)) - df?Open.GetAt(66))
  (opens / df)?Open.GetAt(66) |> shouldEqual (float (opens.GetAt(66)) / df?Open.GetAt(66))
  
[<Test>]
let ``Can perform pointwise numerical operations on two frames`` () =
  let df1 = msft() |> Frame.orderRows
  let df2 = df1 |> Frame.shift 1
  let opens1 = df1?Open
  let opens2 = df2?Open

  (df2 - df1)?Open.GetAt(66) |> shouldEqual (opens2.GetAt(66) - opens1.GetAt(66))
  (df2 + df1)?Open.GetAt(66) |> shouldEqual (opens2.GetAt(66) + opens1.GetAt(66))
  (df2 * df1)?Open.GetAt(66) |> shouldEqual (opens2.GetAt(66) * opens1.GetAt(66))
  (df2 / df1)?Open.GetAt(66) |> shouldEqual (opens2.GetAt(66) / opens1.GetAt(66))

// ------------------------------------------------------------------------------------------------
// Operations - append
// ------------------------------------------------------------------------------------------------

[<Test>]
let ``Can append two frames with disjoint columns`` () = 
  let df1 = Frame.ofColumns [ "A" => series [ for i in 1 .. 5 -> i, i ] ]
  let df2 = Frame.ofColumns [ "B" => series [ for i in 1 .. 5 -> i, i ] ]
  let actual = df1.Append(df2) 
  actual.Rows.[3].GetAt(0) |> shouldEqual (box 3)
  actual.Rows.[3].GetAt(1) |> shouldEqual (box 3)

[<Test>]
let ``Appending works on overlapping frames with missing values`` () =
  let df1 = Frame.ofColumns [ "A" => series [1 => Double.NaN; 2 => 1.0] ]
  let df2 = Frame.ofColumns [ "A" => series [2 => Double.NaN; 1 => 1.0] ]
  let actual = df1.Append(df2)
  actual.Columns.["A"] |> Series.mapValues (unbox<float>) 
  |> shouldEqual (series [1 => 1.0; 2 => 1.0])

[<Test>]
let ``Appending fails on overlapping frames with overlapping values`` () =
  let df1 = Frame.ofColumns [ "A" => series [1 => Double.NaN; 2 => 1.0] ]
  let df2 = Frame.ofColumns [ "A" => series [2 => 1.0] ]
  (fun () -> df1.Append(df2) |> ignore) |> should throw (typeof<InvalidOperationException>)

[<Test>]
let ``Can append two frames with partially overlapping columns`` () = 
  let df1 = Frame.ofColumns [ "A" => series [ for i in 1 .. 5 -> i, i ] ]
  let df2 = Frame.ofColumns 
              [ "A" => series [ 6 => 10 ]
                "B" => series [ for i in 1 .. 5 -> i, i ] ]
  let actual = df1.Append(df2)
  actual.Rows.[3].GetAt(0) |> shouldEqual (box 3)
  actual.Rows.[3].GetAt(1) |> shouldEqual (box 3)
  actual.Rows.[6].GetAt(0) |> shouldEqual (box 10)
  actual.Rows.[6].TryGetAt(1).HasValue |> shouldEqual false

[<Test>]
let ``Can append two frames with single rows and keys with comparison that fails at runtime`` () = 
  let df1 = Frame.ofColumns [ "A" => series [ ([| 0 |], 0) => "A" ] ]
  let df2 = Frame.ofColumns [ "A" => series [ ([| 0 |], 1) => "A" ] ]
  df1.Append(df2).RowKeys |> Seq.length |> shouldEqual 2
 
// ------------------------------------------------------------------------------------------------
// Operations - zip
// ------------------------------------------------------------------------------------------------

[<Test>]
let ``Can inner/outer/left/right join row keys when aligning``()  =
  let df1 = Frame.ofColumns [ "A" => series [ 1=>1; 2=>2 ]]
  let df2 = Frame.ofColumns [ "A" => series [ 2=>2; 3=>3 ]]

  let actualI = (df1, df2) ||> Frame.zipAlign JoinKind.Inner JoinKind.Inner Lookup.Exact (+)
  actualI.RowKeys |> List.ofSeq |> shouldEqual [2]
  let actualO = (df1, df2) ||> Frame.zipAlign JoinKind.Inner JoinKind.Outer Lookup.Exact (+)
  actualO.RowKeys |> List.ofSeq |> shouldEqual [1;2;3]
  let actualL = (df1, df2) ||> Frame.zipAlign JoinKind.Inner JoinKind.Left Lookup.Exact (+)
  actualL.RowKeys |> List.ofSeq |> shouldEqual [1;2]
  let actualR = (df1, df2) ||> Frame.zipAlign JoinKind.Inner JoinKind.Right Lookup.Exact (+)
  actualR.RowKeys |> List.ofSeq |> shouldEqual [2;3]

[<Test>]
let ``Can zip and subtract numerical values in MSFT data set``() = 
  let df1 = msft()
  let df2 = msft()
  let actual = df1.Zip(df2, fun a b -> a - b)
  let values = actual.GetAllValues<int>()
  values |> Seq.length |> shouldEqual (6 * (df1 |> Frame.countRows))
  values |> Seq.forall ((=) 0) |> shouldEqual true

[<Test>]
let ``Can zip and subtract numerical values in MSFT data set; with some rows dropped``() = 
  let df1 = (msft() |> Frame.orderRows).Rows.[DateTime(2000, 1, 1) ..]
  let df2 = msft()
  let values = df1.Zip(df2, fun a b -> a - b).GetAllValues<int>()
  values |> Seq.length |> shouldEqual (6 * (df1 |> Frame.countRows))
  values |> Seq.forall ((=) 0) |> shouldEqual true

[<Test>]
let ``Can zip and subtract numerical values in MSFT data set; with some columns dropped``() = 
  let df1 = msft()
  df1.DropSeries("Adj Close")
  let df2 = msft()
  let zipped = df1.Zip(df2, fun a b -> a - b)
  zipped?``Adj Close`` |> Series.sum |> should (be greaterThan) 0.0
  zipped?Low |> Series.sum |> shouldEqual 0.0

// ------------------------------------------------------------------------------------------------
// Operations - join, align
// ------------------------------------------------------------------------------------------------

let dates =
  [ DateTime(2013,9,9) => 0.0;
    DateTime(2013,9,10) => 1.0;
    DateTime(2013,9,11) => 2.0 ] |> series

let times = 
  [ DateTime(2013,9,9, 9, 31, 59) => 0.5
    DateTime(2013,9,10, 9, 31, 59) => 1.5
    DateTime(2013,9,11, 9, 31, 59) => 2.5 ] |> series

let daysFrame = [ "Days" => dates ] |> Frame.ofColumns
let timesFrame = [ "Times" => times ] |> Frame.ofColumns

[<Test>]
let ``Can left-align ordered frames - nearest smaller returns missing if no smaller value exists``() =
  // every point in timesFrames is later than in daysFrame, there is no point in times 
  // smaller than the first point in days, therefore first value in "Times" column must be missing
  // after left join with NearestSmaller option
  let daysTimesPrevL = 
      (daysFrame, timesFrame) 
      ||> Frame.joinAlign JoinKind.Left Lookup.NearestSmaller

  daysTimesPrevL?Times.TryGetAt(0) |> shouldEqual OptionalValue.Missing
  daysTimesPrevL?Times.TryGetAt(1) |> shouldEqual (OptionalValue 0.5)
  daysTimesPrevL?Times.TryGetAt(2) |> shouldEqual (OptionalValue 1.5)


[<Test>]
let ``Can left-align ordered frames - nearest greater always finds greater value`` () =
  // every point in timesFrames is later than in daysFrame, 
  // all values in Times must be as in original series
  let daysTimesNextL = 
      (daysFrame, timesFrame) 
      ||> Frame.joinAlign JoinKind.Left Lookup.NearestGreater
        
  daysTimesNextL?Times.TryGetAt(0) |> shouldEqual (OptionalValue 0.5)
  daysTimesNextL?Times.TryGetAt(1) |> shouldEqual (OptionalValue 1.5)
  daysTimesNextL?Times.TryGetAt(2) |> shouldEqual (OptionalValue 2.5)

[<Test>]
let ``Can right-align ordered frames - nearest smaller always finds smaller value``() =
  // every point in timesFrames is later than in daysFrame, 
  // all values in Days must be as in original series
  let daysTimesPrevR = 
      (daysFrame, timesFrame) 
      ||> Frame.joinAlign JoinKind.Right Lookup.NearestSmaller
  
  daysTimesPrevR?Days.TryGetAt(0) |> shouldEqual (OptionalValue 0.0)
  daysTimesPrevR?Days.TryGetAt(1) |> shouldEqual (OptionalValue 1.0)
  daysTimesPrevR?Days.TryGetAt(2) |> shouldEqual (OptionalValue 2.0)

[<Test>]
let ``Can right-align ordered frames - nearest greater returns missing if no greater value exists`` () =
  // every point in timesFrames is later than in daysFrame, 
  // last point in Days must be missing after joining
  let daysTimesNextR = 
      (daysFrame, timesFrame) 
      ||> Frame.joinAlign JoinKind.Right Lookup.NearestGreater
  
  daysTimesNextR?Days.TryGetAt(0) |> shouldEqual (OptionalValue 1.0)
  daysTimesNextR?Days.TryGetAt(1) |> shouldEqual (OptionalValue 2.0)
  daysTimesNextR?Days.TryGetAt(2) |> shouldEqual OptionalValue.Missing

[<Test>]
let ``Can join frame with series`` () =
  Check.QuickThrowOnFailure(fun (kind:JoinKind) (lookup:Lookup) (keys1:int[]) (keys2:int[]) (data1:float[]) (data2:float[]) -> 
    let s1 = series (Seq.zip (Seq.sort (Seq.distinct keys1)) data1)
    let s2 = series (Seq.zip (Seq.sort (Seq.distinct keys2)) data2)
    let f1 = Frame.ofColumns ["S1" => s1]
    let f2 = Frame.ofColumns ["S2" => s2]
    let lookup = match kind with JoinKind.Inner | JoinKind.Outer -> Lookup.Exact | _ -> lookup
    f1.Join(f2, kind, lookup) = f1.Join("S2", s2, kind, lookup)
  )

// ------------------------------------------------------------------------------------------------
// Operations - fill
// ------------------------------------------------------------------------------------------------

[<Test>]
let ``Fill missing values using the specified direction``() = 
  let df = 
    [ "A" => series [ for i in 0 .. 100 -> i => if i%3=0 then Double.NaN else float i ] 
      "B" => series [ for i in 0 .. 100 -> i => if i%5=0 then Double.NaN else float i ]
      "C" => series [ for i in 0 .. 100 -> i => if i%20=0 then Double.NaN else float i ]
      "D" => series [ for i in 0 .. 100 -> i => float i ] ]
    |> Frame.ofColumns
  let filled = df |> Frame.fillMissing Direction.Forward
  filled.Rows.[0].As<float>() |> shouldEqual <| series ["A" => Double.NaN; "B" => Double.NaN; "C" => Double.NaN; "D" => 0.0 ]
  filled.Rows.[10].As<float>() |> shouldEqual <| series ["A" => 10.0; "B" => 9.0; "C" => 10.0; "D" => 10.0 ]

[<Test>]
let ``Fill missing values using the specified constant``() = 
  let df = 
    [ "A" => series [ for i in 0 .. 100 -> i => if i%3=0 then Double.NaN else float i ] 
      "B" => series [ for i in 0 .. 100 -> i => if i%5=0 then Double.NaN else float i ]
      "C" => series [ for i in 0 .. 100 -> i => if i%20=0 then Double.NaN else float i ]
      "D" => series [ for i in 0 .. 100 -> i => float i ] ]
    |> Frame.ofColumns
  let filled = df |> Frame.fillMissingWith 0.0
  filled.Rows.[0].As<float>() |> shouldEqual <| series ["A" => 0.0; "B" => 0.0; "C" => 0.0; "D" => 0.0 ]
  filled.Rows.[10].As<float>() |> shouldEqual <| series ["A" => 10.0; "B" => 0.0; "C" => 10.0; "D" => 10.0 ]


// ------------------------------------------------------------------------------------------------
// Operations - join & zip (handling missing values)
// ------------------------------------------------------------------------------------------------

[<Test>]
let ``Left join fills missing values - search for previous when there is no exact key`` () =
  let miss = Frame.ofColumns [ "A" => series [ 1 => 1.0; 2 => Double.NaN; ] ]
  let full = Frame.ofColumns [ "B" => series [ 1 => 2.0; 3 => 3.0 ] ]
  let joined = full.Join(miss, JoinKind.Left, Lookup.NearestSmaller)
  let expected = series [ 1 => 1.0; 3 => 1.0 ]
  joined?A |> shouldEqual expected

[<Test>]
let ``Left join fills missing values - search for previous when there is missing at the exact key`` () =
  let miss = Frame.ofColumns [ "A" => series [ 1 => 1.0; 2 => Double.NaN; ] ]
  let full = Frame.ofColumns [ "B" => series [ 1 => 2.0; 2 => 3.0 ] ]
  let joined = full.Join(miss, JoinKind.Left, Lookup.NearestSmaller)
  let expected = series [ 1 => 1.0; 2 => 1.0 ]
  joined?A |> shouldEqual expected

[<Test>]
let ``Left zip fills missing values - search for previous when there is no exact key`` () =
  let miss = Frame.ofColumns [ "A" => series [ 1 => 1.0; 2 => Double.NaN; ] ]
  let full = Frame.ofColumns [ "A" => series [ 1 => 2.0; 3 => 3.0 ] ]
  let joined = full.Zip(miss, JoinKind.Inner, JoinKind.Left, Lookup.NearestSmaller, fun a b -> a + b)
  let expected = series [ 1 => 3.0; 3 => 4.0 ]
  joined?A |> shouldEqual expected

[<Test>]
let ``Left zip only fills missing values in joined series`` () =
  let miss = Frame.ofColumns [ "A" => series [ 1 => 1.0; 2 => Double.NaN; ] ]
  let full = Frame.ofColumns [ "A" => series [ 1 => 2.0; 2 => 3.0 ] ]
  let joined = miss.Zip(full, JoinKind.Inner, JoinKind.Left, Lookup.NearestSmaller, fun a b -> a + b)
  let expected = series [ 1 => 3.0; 2 => Double.NaN ]
  joined?A |> shouldEqual expected

// ------------------------------------------------------------------------------------------------
// Operations - zip
// ------------------------------------------------------------------------------------------------

[<Test>]
let ``Can ZIP and subtract MSFT stock prices``() =
  let df = msft()
  let actual = (df,df) ||> Frame.zip (fun (v1:float) v2 -> v1 - v2)
  let values = actual.GetAllValues<float>() 
  values |> Seq.length |> should (be greaterThan) 10000
  values |> Seq.sum |> shouldEqual 0.0


// company A has common and preferred stocks, company B only common
// company A trades in US, company B common shares trade in US, while B prefs trade in Israel/GCC 
//   (e.g. B comm is ADR, B pref only local)
// company B did a 1:2 split on Sep-14

// Prices
let pxA =
  [ DateTime(2013,9,10) => 100.0;
    DateTime(2013,9,11) => 101.0;
    DateTime(2013,9,12) => 101.0; // Sat - // for US keep Sat and Sun values until Series.Join supports lookup option with full outer join
    DateTime(2013,9,13) => 101.0; // Sun - // 
    DateTime(2013,9,14) => 102.0; 
    DateTime(2013,9,15) => 103.0; 
    DateTime(2013,9,16) => 104.0;] 
    |> series

let pxB =
  [ DateTime(2013,9,10) => 200.0;
    DateTime(2013,9,11) => 200.0; // Fri
    DateTime(2013,9,12) => 200.0; // Sat
    DateTime(2013,9,13) => 201.0; 
    DateTime(2013,9,14) => 101.0; 
    DateTime(2013,9,15) => 101.5; 
    DateTime(2013,9,16) => 102.0;] 
    |> series

let pxCommons = 
  [ "A" => pxA;
    "B" => pxB;] 
    |> Frame.ofColumns

let pxBpref =
  [ DateTime(2013,9,10) => 20.0;
    //DateTime(2013,9,11) => 20.0; // Fri - // not traded
    //DateTime(2013,9,12) => 20.0; // Sat - // omit these values to illustrate how lookup works in Frame.zipAlign
    DateTime(2013,9,13) => 21.0; 
    DateTime(2013,9,14) => 22.0; 
    DateTime(2013,9,15) => 23.0; 
    DateTime(2013,9,16) => 24.0;] 
    |> series

let pxPrefs = [ "B" => pxBpref] |> Frame.ofColumns

// Shares outstanding
let sharesA = [ DateTime(2012,12,31) => 10.0 ] |> series
let sharesB = [ DateTime(2012,12,31) => 20.0; DateTime(2013,9,14) => 40.0; ] |> series // split
let sharesCommons = [ "A" => sharesA; "B" => sharesB ] |> Frame.ofColumns

let sharesBpref = [ DateTime(2012,12,31) => 20.0 ] |> series
let sharesPrefs = [ "B" => sharesBpref ] |> Frame.ofColumns

// Net debt forecast 2013
let ndA = [ DateTime(2013,12,31) => 100.0] |> series
let ndB = [ DateTime(2013,12,31) => 1000.0 ] |> series
let netDebt =  [ "A" => ndA; "B" => ndB ] |> Frame.ofColumns

[<Test>]
let ``Can zip-align frames with inner-join left-join nearest-smaller options`` () =
  let mktcapA = 
    (pxA, sharesA)
    ||> Series.zipAlignInto JoinKind.Left Lookup.NearestSmaller (fun (l:float) r -> l*r) 
  let mktcapB = 
    (pxB, sharesB)
    ||> Series.zipAlignInto JoinKind.Left Lookup.NearestSmaller (fun (l:float) r -> l*r) 
  
  // calculate stock mktcap 
  let mktCapCommons = 
    (pxCommons, sharesCommons)
    ||> Frame.zipAlign JoinKind.Inner JoinKind.Left Lookup.NearestSmaller (fun (l:float) r -> l*r) 
  
  mktCapCommons?A.GetAt(0) |> shouldEqual 1000.0
  mktCapCommons?A.GetAt(1) |> shouldEqual 1010.0
  mktCapCommons?A.GetAt(2) |> shouldEqual 1010.0
  mktCapCommons?A.GetAt(3) |> shouldEqual 1010.0
  mktCapCommons?A.GetAt(4) |> shouldEqual 1020.0
  mktCapCommons?A.GetAt(5) |> shouldEqual 1030.0
  mktCapCommons?A.GetAt(6) |> shouldEqual 1040.0

  mktCapCommons?B.GetAt(0) |> shouldEqual 4000.0
  mktCapCommons?B.GetAt(1) |> shouldEqual 4000.0
  mktCapCommons?B.GetAt(2) |> shouldEqual 4000.0
  mktCapCommons?B.GetAt(3) |> shouldEqual 4020.0
  mktCapCommons?B.GetAt(4) |> shouldEqual 4040.0
  mktCapCommons?B.GetAt(5) |> shouldEqual 4060.0
  mktCapCommons?B.GetAt(6) |> shouldEqual 4080.0


[<Test>]
let ``Can zip-align frames with different set of columns`` () =
  // calculate stock mktcap 
  let mktCapCommons = 
    (pxCommons, sharesCommons)
    ||> Frame.zipAlign JoinKind.Inner JoinKind.Left Lookup.NearestSmaller (fun (l:float) r -> l*r) 
  // calculate stock mktcap for prefs
  let mktCapPrefs = 
    (pxPrefs, sharesPrefs)
    ||> Frame.zipAlign JoinKind.Inner JoinKind.Left Lookup.NearestSmaller (fun (l:float) r -> l*r) 
  // calculate company mktcap 
  let mktCap = 
    (mktCapCommons, mktCapPrefs)
    ||> Frame.zipAlign JoinKind.Left JoinKind.Left Lookup.NearestSmaller (fun (l:float) r -> l+r) 
  
  mktCap?A.GetAt(0) |> shouldEqual 1000.0
  mktCap?A.GetAt(1) |> shouldEqual 1010.0
  mktCap?A.GetAt(2) |> shouldEqual 1010.0
  mktCap?A.GetAt(3) |> shouldEqual 1010.0
  mktCap?A.GetAt(4) |> shouldEqual 1020.0
  mktCap?A.GetAt(5) |> shouldEqual 1030.0
  mktCap?A.GetAt(6) |> shouldEqual 1040.0

  mktCap?B.GetAt(0) |> shouldEqual 4400.0
  mktCap?B.GetAt(1) |> shouldEqual 4400.0
  mktCap?B.GetAt(2) |> shouldEqual 4400.0
  mktCap?B.GetAt(3) |> shouldEqual 4440.0
  mktCap?B.GetAt(4) |> shouldEqual 4480.0
  mktCap?B.GetAt(5) |> shouldEqual 4520.0
  mktCap?B.GetAt(6) |> shouldEqual 4560.0


[<Test>]
let ``Can zip-align frames with inner-join left-join nearest-greater options`` () =
    // calculate stock mktcap 
  let mktCapCommons = 
    (pxCommons, sharesCommons)
    ||> Frame.zipAlign JoinKind.Inner JoinKind.Left Lookup.NearestSmaller (fun (l:float) r -> l*r) 
  // calculate stock mktcap for prefs
  let mktCapPrefs = 
    (pxPrefs, sharesPrefs)
    ||> Frame.zipAlign JoinKind.Inner JoinKind.Left Lookup.NearestSmaller (fun (l:float) r -> l*r) 
  // calculate company mktcap 
  let mktCap = 
    (mktCapCommons, mktCapPrefs)
    ||> Frame.zipAlign JoinKind.Left JoinKind.Left Lookup.NearestSmaller (fun (l:float) r -> l+r) 
  
  // calculate enterprice value
  let ev = 
    (mktCap, netDebt)
    ||> Frame.zipAlign JoinKind.Inner JoinKind.Left Lookup.NearestGreater (fun (l:float) r -> l+r) // net debt is at the year end
  
  ev?A.GetAt(0) |> shouldEqual 1100.0
  ev?A.GetAt(1) |> shouldEqual 1110.0
  ev?A.GetAt(2) |> shouldEqual 1110.0
  ev?A.GetAt(3) |> shouldEqual 1110.0
  ev?A.GetAt(4) |> shouldEqual 1120.0
  ev?A.GetAt(5) |> shouldEqual 1130.0
  ev?A.GetAt(6) |> shouldEqual 1140.0

  ev?B.GetAt(0) |> shouldEqual 5400.0
  ev?B.GetAt(1) |> shouldEqual 5400.0
  ev?B.GetAt(2) |> shouldEqual 5400.0
  ev?B.GetAt(3) |> shouldEqual 5440.0
  ev?B.GetAt(4) |> shouldEqual 5480.0
  ev?B.GetAt(5) |> shouldEqual 5520.0
  ev?B.GetAt(6) |> shouldEqual 5560.0

// ------------------------------------------------------------------------------------------------
// Operations - transpose
// ------------------------------------------------------------------------------------------------

[<Test>]
let ``Transposed frame created from columns equals frame created from rows (and vice versa)``() =
  let items =
    [ "A" =?> series [1 => 10.0; 2 => 20.0 ]
      "B" =?> series [1 => 30.0; 3 => 40.0 ]
      "C" =?> series [1 => "One"; 2 => "Two" ] ]
  let fromRows = Frame.ofRows items
  let fromCols = Frame.ofColumns items
  fromCols |> Frame.transpose |> shouldEqual fromRows
  fromRows |> Frame.transpose |> shouldEqual fromCols
