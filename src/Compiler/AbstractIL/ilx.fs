// Copyright (c) Microsoft Corporation.  All Rights Reserved.  See License.txt in the project root for license information.

/// Defines an extension of the IL algebra
module internal FSharp.Compiler.AbstractIL.ILX.Types

open FSharp.Compiler.AbstractIL.IL
open Internal.Utilities.Library

let mkLowerName (nm: string) =
    // Use the lower case name of a field or constructor as the field/parameter name if it differs from the uppercase name
    let lowerName = String.uncapitalize nm

    if lowerName = nm then "_" + nm else lowerName

[<Sealed>]
type IlxUnionCaseField(fd: ILFieldDef) =
    let lowerName = mkLowerName fd.Name

    member _.ILField = fd

    member x.Type = x.ILField.FieldType

    member x.Name = x.ILField.Name

    member _.LowerName = lowerName

    override x.ToString() = x.Name

type IlxUnionCase =
    {
        altName: string
        altFields: IlxUnionCaseField[]
        altCustomAttrs: ILAttributes
    }

    member x.FieldDefs = x.altFields

    member x.FieldDef n = x.altFields[n]

    member x.Name = x.altName

    member x.IsNullary = (x.FieldDefs.Length = 0)

    member x.FieldTypes = x.FieldDefs |> Array.map (fun fd -> fd.Type)

    override x.ToString() = x.Name

type IlxUnionHasHelpers =
    | NoHelpers
    | AllHelpers
    | SpecialFSharpListHelpers
    | SpecialFSharpOptionHelpers

type IlxUnionRef = IlxUnionRef of boxity: ILBoxity * ILTypeRef * IlxUnionCase[] * bool (* hasHelpers: *) * IlxUnionHasHelpers

type IlxUnionSpec =
    | IlxUnionSpec of IlxUnionRef * ILGenericArgs

    member x.DeclaringType =
        let (IlxUnionSpec (IlxUnionRef (bx, tref, _, _, _), inst)) = x in mkILNamedTy bx tref inst

    member x.Boxity = let (IlxUnionSpec (IlxUnionRef (bx, _, _, _, _), _)) = x in bx

    member x.TypeRef = let (IlxUnionSpec (IlxUnionRef (_, tref, _, _, _), _)) = x in tref

    member x.GenericArgs = let (IlxUnionSpec (_, inst)) = x in inst

    member x.AlternativesArray =
        let (IlxUnionSpec (IlxUnionRef (_, _, alts, _, _), _)) = x in alts

    member x.IsNullPermitted =
        let (IlxUnionSpec (IlxUnionRef (_, _, _, np, _), _)) = x in np

    member x.HasHelpers = let (IlxUnionSpec (IlxUnionRef (_, _, _, _, b), _)) = x in b

    member x.Alternatives = Array.toList x.AlternativesArray

    member x.Alternative idx = x.AlternativesArray[idx]

    member x.FieldDef idx fidx = x.Alternative(idx).FieldDef(fidx)

    override x.ToString() = x.TypeRef.Name

type IlxClosureLambdas =
    | Lambdas_forall of ILGenericParameterDef * IlxClosureLambdas
    | Lambdas_lambda of ILParameter * IlxClosureLambdas
    | Lambdas_return of ILType

type IlxClosureApps =
    | Apps_tyapp of ILType * IlxClosureApps
    | Apps_app of ILType * IlxClosureApps
    | Apps_done of ILType

let rec instAppsAux n inst apps =
    match apps with
    | Apps_tyapp (ty, rest) -> Apps_tyapp(instILTypeAux n inst ty, instAppsAux n inst rest)
    | Apps_app (domainTy, rest) -> Apps_app(instILTypeAux n inst domainTy, instAppsAux n inst rest)
    | Apps_done retTy -> Apps_done(instILTypeAux n inst retTy)

let rec instLambdasAux n inst lambdas =
    match lambdas with
    | Lambdas_forall (gpdef, bodyTy) -> Lambdas_forall(gpdef, instLambdasAux n inst bodyTy)
    | Lambdas_lambda (pdef, bodyTy) ->
        Lambdas_lambda(
            { pdef with
                Type = instILTypeAux n inst pdef.Type
            },
            instLambdasAux n inst bodyTy
        )
    | Lambdas_return retTy -> Lambdas_return(instILTypeAux n inst retTy)

type IlxClosureFreeVar =
    {
        fvName: string
        fvCompilerGenerated: bool
        fvType: ILType
    }

    override x.ToString() = x.fvName

let mkILFreeVar (name, compgen, ty) =
    {
        fvName = name
        fvCompilerGenerated = compgen
        fvType = ty
    }

type IlxClosureRef = IlxClosureRef of ILTypeRef * IlxClosureLambdas * IlxClosureFreeVar[]

type IlxClosureSpec =
    | IlxClosureSpec of IlxClosureRef * ILGenericArgs * ILType * useStaticField: bool

    member x.TypeRef = let (IlxClosureRef (tref, _, _)) = x.ClosureRef in tref

    member x.ILType = let (IlxClosureSpec (_, _, ty, _)) = x in ty

    member x.ClosureRef = let (IlxClosureSpec (cloref, _, _, _)) = x in cloref

    member x.FormalFreeVars = let (IlxClosureRef (_, _, fvs)) = x.ClosureRef in fvs

    member x.FormalLambdas = let (IlxClosureRef (_, lambdas, _)) = x.ClosureRef in lambdas

    member x.GenericArgs = let (IlxClosureSpec (_, inst, _, _)) = x in inst

    static member Create(cloref, inst, useStaticField) =
        let (IlxClosureRef (tref, _, _)) = cloref
        IlxClosureSpec(cloref, inst, mkILBoxedType (mkILTySpec (tref, inst)), useStaticField)

    member x.Constructor =
        let cloTy = x.ILType
        let fields = x.FormalFreeVars
        mkILCtorMethSpecForTy (cloTy, fields |> Array.map (fun fv -> fv.fvType) |> Array.toList)

    member x.UseStaticField =
        let (IlxClosureSpec (_, _, _, useStaticField)) = x
        useStaticField

    member x.GetStaticFieldSpec() =
        assert x.UseStaticField
        let formalCloTy = mkILFormalBoxedTy x.TypeRef (mkILFormalTypars x.GenericArgs)
        mkILFieldSpecInTy (x.ILType, "@_instance", formalCloTy)

    override x.ToString() = x.TypeRef.ToString()

// Define an extension of the IL algebra of type definitions
type IlxClosureInfo =
    {
        cloStructure: IlxClosureLambdas
        cloFreeVars: IlxClosureFreeVar[]
        cloCode: Lazy<ILMethodBody>
        cloUseStaticField: bool
    }

type IlxUnionInfo =
    {
        UnionCasesAccessibility: ILMemberAccess

        HelpersAccessibility: ILMemberAccess

        HasHelpers: IlxUnionHasHelpers

        GenerateDebugProxies: bool

        DebugDisplayAttributes: ILAttribute list

        UnionCases: IlxUnionCase[]

        IsNullPermitted: bool

        DebugPoint: ILDebugPoint option

        DebugImports: ILDebugImports option
    }

    override _.ToString() = "<union info>"

// --------------------------------------------------------------------
// Define these as extensions of the IL types
// --------------------------------------------------------------------

let destTyFuncApp input =
    match input with
    | Apps_tyapp (b, c) -> b, c
    | _ -> failwith "destTyFuncApp"

let mkILFormalCloRef gparams csig useStaticField =
    IlxClosureSpec.Create(csig, mkILFormalGenericArgs 0 gparams, useStaticField)

let actualTypOfIlxUnionField (cuspec: IlxUnionSpec) idx fidx =
    instILType cuspec.GenericArgs (cuspec.FieldDef idx fidx).Type
