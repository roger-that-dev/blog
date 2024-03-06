open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting

/// Super simple static file server for local testing. Amazing!
///
/// Expects to be run in a folder with an "output" directory
/// which contains the static site.
[<EntryPoint>]
let main args =
    WebHost
        .CreateDefaultBuilder(args)
        .Configure(fun config -> config.UseDefaultFiles().UseStaticFiles().UseStatusCodePages() |> ignore)
        .UseWebRoot("output")
        .Build()
        .Run()

    0
