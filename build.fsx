//////////////////////////////////////////////////////////////////////
// DEPENDENCIES
//////////////////////////////////////////////////////////////////////
#r "tools/Cake.Core.0.20.0/lib/net45/Cake.Core.dll"
#r "tools/Cake.Common.0.20.0/lib/net45/Cake.Common.dll"
#r "tools/Cake.Bridge.0.0.1-alpha/lib/net45/Cake.Bridge.dll"

//////////////////////////////////////////////////////////////////////
// NAMESPACE IMPORTS
//////////////////////////////////////////////////////////////////////
open Cake.Common
open Cake.Common.Diagnostics
open Cake.Common.IO
open Cake.Common.Tools.DotNetCore
open Cake.Common.Tools.DotNetCore.Build
open Cake.Common.Tools.DotNetCore.Pack
open Cake.Core
open Cake.Core.IO
open System
open System.Linq
//open CakeBridge

// TODO:
//  1. Improve style
//  2. Remove the need to specify CakeBridge everywhere
//  3. More idiomatic handling of global state


// "Magic" to handle implicit conversion (which F# isn't really supporting)
let inline (!>) (x:^a) : ^b = ((^a or ^b) : (static member op_Implicit : ^a -> ^b) x)
let context                 = CakeBridge.Context

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////
let target        = context.Argument("target", "Default")
let configuration = context.Argument("configuration", "Release")

//////////////////////////////////////////////////////////////////////
// GLOBALS
//////////////////////////////////////////////////////////////////////
let directoryPath           = !> context.Directory("./nuget")
let nugetRoot               = context.MakeAbsolute(directoryPath)
let mutable solution        = null : FilePath
let mutable semVersion      = null : string
let mutable assemblyVersion = null : string
let mutable fileVersion     = null : string

//////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
//////////////////////////////////////////////////////////////////////

CakeBridge.Setup(fun context ->
    context.Information("Setting up...")
    solution <- context.GetFiles("./src/*.sln").FirstOrDefault()
    if solution = null then failwith "Failed to find solution"

    let releaseNotes  =   context.ParseReleaseNotes(!> "./ReleaseNotes.md")
    assemblyVersion   <-  releaseNotes.Version.ToString()
    fileVersion       <-  assemblyVersion
    semVersion        <-  assemblyVersion + "-alpha"

    context.Information("Executing build {0}...", semVersion)
  )

CakeBridge.Teardown(fun context ->
    context.Information("Tearing down...")
  )

//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

let clean = CakeBridge.Task("Clean").Does(fun () ->
    context.CleanDirectories("./src/**/bin/" + configuration)
    context.CleanDirectories("./src/**/obj/" + configuration)
    context.CleanDirectory(nugetRoot)
  )

let restore = CakeBridge.Task("Restore").IsDependentOn(clean).Does(fun () ->
    context.DotNetCoreRestore(solution.FullPath)
  )

let build = CakeBridge.Task("Build").IsDependentOn(restore).Does(fun () ->
    context.DotNetCoreBuild(
      solution.FullPath,
      DotNetCoreBuildSettings(  Configuration         = configuration,
                                ArgumentCustomization = (fun args -> args.Append("/p:Version={0}", semVersion)                                          .Append("/p:AssemblyVersion={0}", assemblyVersion)                                          .Append("/p:FileVersion={0}", fileVersion))))
  )

let pack = CakeBridge.Task("Pack").IsDependentOn(build).Does(fun () ->
    if context.DirectoryExists(nugetRoot) |> not then
        context.CreateDirectory(nugetRoot)

    for project in context.GetFiles("./src/**/*.csproj").Where(fun file -> file.FullPath.EndsWith("Tests") |> not) do
        context.DotNetCorePack(
          project.FullPath,
          DotNetCorePackSettings(
            Configuration         = configuration,
            OutputDirectory       = nugetRoot,
            NoBuild               = true,
            ArgumentCustomization = (fun args -> args.Append("/p:Version={0}", semVersion).Append("/p:AssemblyVersion={0}", assemblyVersion).Append("/p:FileVersion={0}", fileVersion))
          )
    )
  )

CakeBridge.Task("Default")
    .IsDependentOn(pack)

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////
CakeBridge.RunTarget(target)
