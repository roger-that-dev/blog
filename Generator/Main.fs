namespace Blog

module Main =

    type FixedPage =
        { Name: string
          Template: string
          URL: string }

    type SinglePage =
        { Name: string
          Template: string
          PageGenerator: string -> string -> List<Post.Post> -> Result<unit, Shared.Failure> }

    type MultiPage =
        { Name: string
          Template: string
          PageGenerator: string -> string -> List<Post.Post> -> Result<unit, List<Shared.Failure>> }

    type Page =
        | FixedPage of FixedPage
        | SinglePage of SinglePage
        | MultiPage of MultiPage

    open System.IO
    open FSharpx.Result
    open Shared

    /// Given a path to a folder, recursively enumerates all of the markdown files in the folder and all
    /// sub folders. If the path cannot be found or there is any other IO exception then an Error is
    /// returned.
    let enumerateMarkdownFiles directory =
        try
            let fileNames =
                Directory.GetFiles(directory, "*.md", SearchOption.AllDirectories)
                |> List.ofArray

            Ok fileNames
        with e ->
            Error(Failure e.Message)

    let pages: Page list =
        [ FixedPage
              { Name = "About"
                Template = "templates/pages/about.tmpl"
                URL = "about" }
          MultiPage
              { Name = "Post"
                Template = "templates/pages/post.tmpl"
                PageGenerator = Generator.postGenerator } ]

    /// Entry point for when testing with FSI.
    let start markdownPath outputPath =
        result {
            let! markdownFiles = enumerateMarkdownFiles markdownPath |> Result.mapError List.singleton
            let! posts = markdownFiles |> List.map Post.fromMarkdown |> Result.combine

            let results =
                pages
                |> List.map (fun page ->
                    match page with
                    | FixedPage p ->
                        Generator.staticGenerator p.Template outputPath p.URL
                        |> Result.mapError List.singleton
                    | MultiPage p -> p.PageGenerator p.Template outputPath posts
                    | SinglePage p -> p.PageGenerator p.Template outputPath posts |> Result.mapError List.singleton)
                |> Result.combine

            return results
        }

    // start "data" "output" "templates/test.tmpl"

    open Argu

    type Arguments =
        | [<Mandatory>] Input_Directory of path: string
        | [<Mandatory>] Output_Directory of path: string

        interface IArgParserTemplate with
            member s.Usage =
                match s with
                | Input_Directory _ -> "specify a directory containing markdown files"
                | Output_Directory _ -> "specify a directory where the output HTML files will be written"

    [<EntryPoint>]
    let main args =
        let parser = ArgumentParser.Create<Arguments>(programName = "blog")
        let results = parser.Parse args
        let markdownPath = results.GetResult(Input_Directory, "")
        let outputPath = results.GetResult(Output_Directory, "")
        printfn "Starting blog generator..."

        match start markdownPath outputPath with
        | Ok _ -> printfn "Blog written successfully to %s." outputPath
        | Error e -> List.iter (printfn "%O") e

        0

// Need a base template for all of the pages, header and footer
// How are the mark down files stored? I guess they can be sorted however i please as the root directory is fully traversed.
// Create the directories for each post yyyy/mm/dd/slug index.html any images or other assets are also copied into this directory too
// Create the index file which shows the first 10 posts.
// Create an index for each year month and day
// Create a tags page which shows all tags
// Create a page which shows the posts for each tag
// Posts are ordered by Date time
// No need for any pagination yet
