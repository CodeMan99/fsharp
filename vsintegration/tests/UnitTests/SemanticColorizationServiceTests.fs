﻿// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.
namespace Microsoft.VisualStudio.FSharp.Editor.Tests.Roslyn

open System
open NUnit.Framework
open Microsoft.VisualStudio.FSharp.Editor
open FSharp.Compiler.SourceCodeServices
open FSharp.Compiler.Text
open Microsoft.CodeAnalysis.Text
open Microsoft.CodeAnalysis.Classification

[<TestFixture; Category "Roslyn Services">]
type SemanticClassificationServiceTests() =
    let filePath = "C:\\test.fs"

    let projectOptions = { 
        ProjectFileName = "C:\\test.fsproj"
        ProjectId = None
        SourceFiles =  [| filePath |]
        ReferencedProjects = [| |]
        OtherOptions = [| |]
        IsIncompleteTypeCheckEnvironment = true
        UseScriptResolutionRules = false
        LoadTime = DateTime.MaxValue
        UnresolvedReferences = None
        OriginalLoadReferences = []
        ExtraProjectInfo = None
        Stamp = None
    }

    let checker = FSharpChecker.Create()
    let perfOptions = { LanguageServicePerformanceOptions.Default with AllowStaleCompletionResults = false }

    let getRanges (source: string) : struct (range * SemanticClassificationType) list =
        asyncMaybe {

            let! _, _, checkFileResults = checker.ParseAndCheckDocument(filePath, 0, SourceText.From(source), projectOptions, perfOptions, "")
            return checkFileResults.GetSemanticClassification(None)
        } 
        |> Async.RunSynchronously
        |> Option.toList
        |> List.collect Array.toList

    let verifyClassificationAtEndOfMarker(fileContents: string, marker: string, classificationType: string) =
        let text = SourceText.From(fileContents)
        let ranges = getRanges fileContents
        let line = text.Lines.GetLinePosition (fileContents.IndexOf(marker) + marker.Length - 1)
        let markerPos = Pos.mkPos (Line.fromZ line.Line) (line.Character + marker.Length - 1)
        match ranges |> List.tryFind (fun struct (range, _) -> Range.rangeContainsPos range markerPos) with
        | None -> Assert.Fail("Cannot find colorization data for end of marker")
        | Some(_, ty) -> Assert.AreEqual(classificationType, FSharpClassificationTypes.getClassificationTypeName ty, "Classification data doesn't match for end of marker")

    let verifyNoClassificationDataAtEndOfMarker(fileContents: string, marker: string, classificationType: string) =
        let text = SourceText.From(fileContents)
        let ranges = getRanges fileContents
        let line = text.Lines.GetLinePosition (fileContents.IndexOf(marker) + marker.Length - 1)
        let markerPos = Pos.mkPos (Line.fromZ line.Line) (line.Character + marker.Length - 1)
        let anyData = ranges |> List.exists (fun struct (range, sct) -> Range.rangeContainsPos range markerPos && ((FSharpClassificationTypes.getClassificationTypeName sct) = classificationType))
        Assert.False(anyData, "Classification data was found when it wasn't expected.")

    [<TestCase("(*1*)", ClassificationTypeNames.StructName)>]
    [<TestCase("(*2*)", ClassificationTypeNames.ClassName)>]
    [<TestCase("(*3*)", ClassificationTypeNames.StructName)>]
    [<TestCase("(*4*)", ClassificationTypeNames.ClassName)>]
    [<TestCase("(*5*)", ClassificationTypeNames.StructName)>]
    [<TestCase("(*6*)", ClassificationTypeNames.StructName)>]
    [<TestCase("(*7*)", ClassificationTypeNames.ClassName)>]
    member _.Measured_Types(marker: string, classificationType: string) =
        verifyClassificationAtEndOfMarker(
                """#light (*Light*)
                open System
                
                [<MeasureAnnotatedAbbreviation>] type (*1*)Guid<[<Measure>] 'm> = Guid
                [<MeasureAnnotatedAbbreviation>] type (*2*)string<[<Measure>] 'm> = string
                
                let inline cast<'a, 'b> (a : 'a) : 'b = (# "" a : 'b #)
                
                type Uom =
                    static member inline tag<[<Measure>]'m> (x : Guid) : (*3*)Guid<'m> = cast x
                    static member inline tag<[<Measure>]'m> (x : string) : (*4*)string<'m> = cast x
                
                type [<Measure>] Ms
                
                let i: (*5*)int<Ms> = 1<Ms>
                let g: (*6*)Guid<Ms> = Uom.tag Guid.Empty
                let s: (*7*)string<Ms> = Uom.tag "foo" """,
            marker, 
            classificationType)

    [<TestCase("(*1*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*2*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*3*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*4*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*5*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*6*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*7*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*8*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*9*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*10*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*11*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*12*)", FSharpClassificationTypes.MutableVar)>]
    member _.MutableValues(marker: string, classificationType: string) =
        let sourceText ="""
type R1 = { mutable (*1*)Doop: int}
let r1 = { (*2*)Doop = 12 }
r1.Doop

let mutable (*3*)first = 12

printfn "%d" (*4*)first

let g ((*5*)xRef: outref<int>) = (*6*)xRef <- 12

let f() =
    let (*7*)second = &first
    let (*8*)third: outref<int> = &first
    printfn "%d%d" (*9*)second (*10*)third

type R = { (*11*)MutableField: int ref }
let r = { (*12*)MutableField = ref 12 }
r.MutableField
r.MutableField := 3
"""
        verifyClassificationAtEndOfMarker(sourceText, marker, classificationType)


    [<TestCase("(*1*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*2*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*3*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*4*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*5*)", FSharpClassificationTypes.MutableVar)>]
    [<TestCase("(*6*)", FSharpClassificationTypes.MutableVar)>]
    member _.NoInrefsExpected(marker: string, classificationType: string) =
        let sourceText = """
let f (item: (*1*)inref<int>) = printfn "%d" (*2*)item
let g() =
    let x = 1
    let y = 2
    let (*3*)xRef = &x
    let (*4*)yRef: inref<int> = &y
    f (*5*)&xRef
    f (*6*)&yRef
"""
        verifyNoClassificationDataAtEndOfMarker(sourceText, marker, classificationType)