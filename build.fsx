#load "cake.fsx"

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

open CakeAdapter.CakeModule

// Execute script with: fsi build.fsx

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////
let target        = context.Argument("target", "Default")
let configuration = context.Argument("configuration", "Release")

//////////////////////////////////////////////////////////////////////
// GLOBALS
//////////////////////////////////////////////////////////////////////
let directoryPath           = !> (context.Directory "./nuget")
let nugetRoot               = context.MakeAbsolute directoryPath

type ProjectInfo =
  {
    AssemblyVersion : string
    FileVersion     : string
    SemVersion      : string
    Solution        : FilePath
  }

let mutable projectInfo     = None

let argumentCustomizer      = Func<ProcessArgumentBuilder,ProcessArgumentBuilder> (fun args ->
                                let p = projectInfo.Value
                                args
                                  .Append("/p:Version={0}"        , p.SemVersion      )
                                  .Append("/p:AssemblyVersion={0}", p.AssemblyVersion )
                                  .Append("/p:FileVersion={0}"    , p.FileVersion     ))

//////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
//////////////////////////////////////////////////////////////////////

setup (fun context ->
    context.Information "Setting up..."

    let solution        =
      match context.GetFiles "./src/*.sln" |> Seq.tryHead with
      | Some solution -> solution
      | None          -> failwith "Failed to find solution"

    let releaseNotes    = context.ParseReleaseNotes(!> "./ReleaseNotes.md")
    let assemblyVersion = string releaseNotes.Version
    let fileVersion     = assemblyVersion
    let semVersion      = assemblyVersion + "-alpha"

    projectInfo <- Some {
        AssemblyVersion = assemblyVersion
        FileVersion     = fileVersion
        SemVersion      = semVersion
        Solution        = solution
      }

    context.Information("Executing build {0}...", semVersion)
  )

tearDown (fun context ->
    context.Information "Tearing down..."
  )
//////////////////////////////////////////////////////////////////////
// TASKS
//////////////////////////////////////////////////////////////////////

let clean =
  task "Clean"
  |> does (fun () ->
    context.CleanDirectories("./src/**/bin/" + configuration)
    context.CleanDirectories("./src/**/obj/" + configuration)
    context.CleanDirectory nugetRoot
  )

let restore =
  task "Restore"
  |> isDependentOn clean
  |> does (fun () ->
    context.DotNetCoreRestore projectInfo.Value.Solution.FullPath
  )

let build =
  task "Build"
  |> isDependentOn restore
  |> does (fun () ->
    context.DotNetCoreBuild(
      projectInfo.Value.Solution.FullPath         ,
      DotNetCoreBuildSettings(
        Configuration         = configuration     ,
        ArgumentCustomization = argumentCustomizer))
  )

let pack =
  task "Pack"
  |> isDependentOn build
  |> does (fun () ->
    if context.DirectoryExists nugetRoot |> not then context.CreateDirectory nugetRoot

    let projectFiles =
      context.GetFiles "./src/**/*.csproj"
      |> Seq.filter (fun file -> file.FullPath.EndsWith "Tests" |> not)
      |> Seq.toArray

    for project in projectFiles do
        context.DotNetCorePack(
          project.FullPath                            ,
          DotNetCorePackSettings(
            Configuration         = configuration     ,
            OutputDirectory       = nugetRoot         ,
            NoBuild               = true              ,
            ArgumentCustomization = argumentCustomizer))
  )

task "Default"
  |> isDependentOn pack

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////
runTarget target
