namespace Blog

module Shared =

    type Failure =
        | Failure of string
        | FailureWithMessage of string * string

    type Result<'a, 'b> with

        /// Turns a list of Results into a Result of lists. Useful when we need all Results to be Ok
        /// and care about visibility of all Errors.
        ///
        /// If there are errors, it returns all the errors. If there are no errors then it returns
        /// all of the successes. Example:
        ///
        /// [Ok 1 ; Ok 2] -> [Ok 2 ; Ok 1]
        /// [Ok 1 ; Error "boom" ; Error "bam" ; Ok 2] -> [Error "boom", Error "bam"]
        ///
        /// NOTE: Returns the Results in reverse order.
        static member combine(input: Result<'a, 'b> list) =
            let folder acc input =
                match acc, input with
                | Error x, Ok _ -> Error x
                | Error x, Error y -> Error(y :: x)
                | Ok x, Ok y -> Ok(y :: x)
                | Ok _, Error y -> Error [ y ]

            List.fold folder (Ok []) input

module Yaml =

    open YamlDotNet
    open YamlDotNet.Serialization.NamingConventions

    let deserialise<'T> (input: string) =
        let deserialiser =
            Serialization
                .DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()

        try
            Ok(deserialiser.Deserialize<'T> input)
        with e ->
            let str = sprintf "could not deserialise front matter: %s" e.Message
            Error str

module Markdown =

    open System.IO
    open Markdig

    let private parse (pipeline: MarkdownPipeline) fileName =
        let parseFn s = Markdown.Parse(s, pipeline)
        // TODO: Handle any exceptions from ReadAllText.
        fileName |> File.ReadAllText |> parseFn

    let private render (pipeline: MarkdownPipeline) ast =
        let sw = new StringWriter()
        let renderer = new Renderers.HtmlRenderer(sw)
        pipeline.Setup(renderer)
        renderer.Render(ast) |> ignore
        sw.Flush()
        sw.ToString()

    let private frontMatter (block: Syntax.Block) =
        match block with
        | :? Extensions.Yaml.YamlFrontMatterBlock as b -> Ok(b.Lines.ToString())
        | _ -> Error "front matter missing"

    let newParser =
        let pipeline = MarkdownPipelineBuilder().UseYamlFrontMatter().Build()
        let rendererWithPipeline = render pipeline
        let parserWithPipeline = parse pipeline

        fun fileName ->
            let ast = parserWithPipeline fileName
            ast |> Seq.head |> frontMatter, ast |> rendererWithPipeline

module Post =

    open Shared

    [<CLIMutable>]
    type FrontMatter =
        { Title: string
          Author: string
          Slug: string
          Source: string
          Date: System.DateTime
          Tags: string array }

    type Post =
        { FrontMatter: FrontMatter
          Content: string }

        static member withSource(post: Post) = post.FrontMatter.Source, post

    let private validateTitle input =
        if input.Title = "" then Error "title missing" else Ok input

    let private validateSlug input =
        if input.Slug = "" then Error "slug missing" else Ok input

    let private validate input =
        input |> validateTitle |> Result.bind validateSlug

    let fromMarkdown fileName =
        let parse = Markdown.newParser
        let yaml, html = parse fileName

        yaml
        |> Result.bind Yaml.deserialise<FrontMatter>
        |> Result.bind validate
        |> Result.map (fun it -> { FrontMatter = it; Content = html })
        |> Result.mapError (fun it -> FailureWithMessage(fileName, it))

module Templates =

    open Scriban
    open System.IO
    open Scriban.Runtime
    open System.Threading.Tasks

    let initialTemplates =
        Map
            [ "base", "templates/base.tmpl"
              "partials/post", "templates/partials/post.tmpl" ]

    /// Map of name to filename. E.g.
    /// base -> templates/base.tmpl
    /// page -> templates/post.tmpl / index.tmpl
    /// partials/xyz -> templates/partials/xyz.tmpl ->
    type IncludeTemplates(templateMap: Map<string, string>) =

        interface ITemplateLoader with
            member this.GetPath(context, callerSpan, templateName) =
                match templateMap.TryFind(templateName) with
                | Some path -> path
                | None -> ""

            member this.Load(context, callerSpan, templatePath) = File.ReadAllText(templatePath)

            member this.LoadAsync(context, callerSpan, templatePath) =
                ValueTask<string>(File.ReadAllTextAsync(templatePath))

    let newTemplate =
        let path = initialTemplates.Item("base")
        Template.Parse(File.ReadAllText(path), path)

    let newContext templatePath =
        let context = TemplateContext(MemberRenamer = fun m -> m.Name)
        let templates = initialTemplates.Add("main", templatePath)
        context.TemplateLoader <- IncludeTemplates(templates)
        System.Console.WriteLine(templates)
        context

module Generator =

    open System.IO
    open Scriban.Runtime
    open Shared
    open FSharpx.Result

    let createDir path =
        try
            Directory.CreateDirectory(path) |> ignore
            Ok()
        with e ->
            let str = sprintf "could not create output directory: %s" e.Message
            Error(Failure str)

    let writeFile path contents =
        try
            File.WriteAllText(path, contents)
            Ok()
        with e ->
            let str = sprintf "could not write to %s: %s" path e.Message
            Error(Failure str)

    let staticGenerator templatePath outputPath url =
        result {
            let template = Templates.newTemplate
            let context = Templates.newContext templatePath
            let result = template.Render(context)
            let folder = Path.Combine(outputPath, url)
            let combined = Path.Combine(folder, "index.html")
            do! createDir folder
            do! writeFile combined result
        }

    let postGenerator templatePath outputPath (posts: Post.Post list) =
        let template = Templates.newTemplate
        let context = Templates.newContext templatePath

        let postPath outputPath (p: Post.Post) =
            let fm = p.FrontMatter
            sprintf "%s/%d/%d/%d/%s" outputPath fm.Date.Year fm.Date.Month fm.Date.Day fm.Slug

        let generate (post: Post.Post) =
            result {
                System.Console.WriteLine(post)
                let scriptObject = ScriptObject()
                scriptObject.Add("Post", post)
                context.PushGlobal(scriptObject)
                let result = template.Render(context)
                let postFolder = postPath outputPath post
                let combined = Path.Combine(postFolder, "index.html")
                do! createDir postFolder
                do! writeFile combined result
            }

        result {
            let! d = posts |> List.map generate |> Result.combine |> Result.map ignore
            return d
        }

// let generateIndex (posts: Post.Post list) =
//     let firstTen = posts |> List.sortBy (fun it -> it.FrontMatter.Date) |> List.take 10 |>
