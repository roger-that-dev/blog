namespace Blog

module Shared =

    /// When we build the site, we might have more than one failure.
    /// We might also want to note in which faile the failure occured.
    type Failure =
        | Failure of string
        | FailureWithSourceFile of string * Failure
        | MultipleFailures of Failure list

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

    /// Boilerplate to catch exceptions into result types which are used for
    /// monadic error handling.
    let tryWith<'a> (f: unit -> 'a) errMsg : Result<'a, Failure> =
        try
            Ok(f ())
        with ex ->
            let msg = sprintf "%s: %s" errMsg ex.Message
            Error(Failure msg)

    let readAllText path =
        tryWith (fun () -> System.IO.File.ReadAllText(path)) (sprintf "could not read file %s" path)

module Yaml =

    open Shared
    open YamlDotNet
    open YamlDotNet.Serialization.NamingConventions

    /// For deserialising a YAML string.
    let deserialise<'T> (input: string) =
        let deserialiser =
            Serialization
                .DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()

        tryWith (fun () -> deserialiser.Deserialize<'T> input) "could not deserialise front matter"

module Markdown =

    open Shared
    open System.IO
    open Markdig
    open FSharpx.Result

    type Parsed = { Yaml: string; Html: string }

    /// Parses a string into a Markdig abstract syntax tree, which can
    /// be used for rendering and pulling out the front matter.
    let private parse (pipeline: MarkdownPipeline) fileName =
        // Markdown.Parse should never throw.
        let parseFn s = Markdown.Parse(s, pipeline)
        fileName |> readAllText |> Result.map parseFn

    /// Renders a Markdig abstract syntax tree to a string.
    let private render (pipeline: MarkdownPipeline) ast =
        let sw = new StringWriter()
        let renderer = new Renderers.HtmlRenderer(sw)
        pipeline.Setup(renderer)
        renderer.Render(ast) |> ignore
        sw.Flush()
        sw.ToString()

    /// Checks whether the supplied syntax block is a front matter and if so
    /// returns it as a sstring. A missing front matter will report back an
    /// error for the markdown file in question when generating the website.
    let private frontMatter (block: Syntax.Block) =
        match block with
        | :? Extensions.Yaml.YamlFrontMatterBlock as b -> Ok(b.Lines.ToString())
        | _ -> Error(Failure "front matter missing")

    /// Creates an instance of a parser to be used later. We create the pipeline
    /// once and close over it to save doing it for every markdown file.
    let newParser =
        let pipeline = MarkdownPipelineBuilder().UseYamlFrontMatter().Build()
        let rendererWithPipeline = render pipeline
        let parserWithPipeline = parse pipeline

        // Returns the front matter and html if there's
        // no error when parsing the front matter.
        fun fileName ->
            result {
                let! ast = parserWithPipeline fileName
                let! yaml = ast |> Seq.head |> frontMatter
                let html = ast |> rendererWithPipeline

                return { Yaml = yaml; Html = html }
            }

module Post =

    open Shared
    open Markdown
    open FSharpx.Result

    /// Contains all the items expected to be in the FrontMatter.
    /// If any are missing or don't pass teh validation checks
    /// then an error is logged.
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
          Content: string
          URL: string }

        static member withSource(post: Post) = post.FrontMatter.Source, post

    let private validateTitle input =
        if input.Title = "" then
            Error(Failure "title missing")
        else
            Ok input

    let private validateSlug input =
        if input.Slug = "" then
            Error(Failure "slug missing")
        else
            Ok input

    /// Composes a validator out of smaller validator functions. Each one returns
    /// the original Record type but fails fast if an error is encountered.
    let private validate input =
        input |> validateTitle |> Result.bind validateSlug

    let fromMarkdown (parseFn: string -> Result<Parsed, Failure>) fileName =
        let post =
            result {
                let! { Yaml = yaml; Html = html } = parseFn fileName
                let! unvalidated = Yaml.deserialise<FrontMatter> yaml
                let! validated = validate unvalidated

                let url =
                    sprintf "%d/%d/%d/%s" validated.Date.Year validated.Date.Month validated.Date.Day validated.Slug

                return
                    { FrontMatter = validated
                      Content = html
                      URL = url }
            }

        post |> Result.mapError (fun it -> FailureWithSourceFile(fileName, it))


module Templates =

    open Shared
    open Scriban
    open System.IO
    open Scriban.Runtime
    open System.Threading.Tasks

    /// A map of all the templates the site uses. When a new (non-page)
    /// templated is added then it must be added here.
    let initialTemplates =
        Map
            [ "base", "templates/base.tmpl"
              "partials/post", "templates/partials/post.tmpl" ]

    /// Map of name to filename. E.g.
    /// base -> templates/base.tmpl
    /// page/post -> templates/page/post.tmpl
    /// page/home -> templates/page/home.tmpl
    /// partials/xyz -> templates/partials/xyz.tmpl
    ///
    /// This type is used by Scriban to load templates at runtime. Apparently,
    /// `getPath` and `load` are split up to allow caching of the templates.
    ///
    /// Presumably Scriban handles any exceptions here or they bubble up
    /// to the Render function call for me to handle.
    type IncludeTemplates(templateMap: Map<string, string>) =

        interface ITemplateLoader with
            member this.GetPath(context, callerSpan, templateName) =
                match templateMap.TryFind(templateName) with
                | Some path -> path
                | None -> invalidArg "templateName" (sprintf "could not find template with name %s" templateName)

            member this.Load(context, callerSpan, templatePath) = File.ReadAllText(templatePath)

            member this.LoadAsync(context, callerSpan, templatePath) =
                ValueTask<string>(File.ReadAllTextAsync(templatePath))

    let newTemplate =
        let path = initialTemplates.Item("base")
        readAllText path |> Result.map (fun it -> Template.Parse(it, path))

    let newContext templatePath =
        let context = TemplateContext(MemberRenamer = fun m -> m.Name)
        let templates = initialTemplates.Add("main", templatePath)
        context.TemplateLoader <- IncludeTemplates(templates)
        context

    let addData<'T> (context: TemplateContext) key (data: 'T) =
        let scriptObject = ScriptObject()
        scriptObject.Add(key, data)
        context.PushGlobal(scriptObject)

    let tryRender (template: Scriban.Template) context =
        tryWith (fun () -> template.Render(context)) "template rendering failed"


module Generator =

    open System.IO
    open Shared
    open FSharpx.Result

    let createDir path =
        tryWith (fun () -> Directory.CreateDirectory(path) |> ignore) "could not create output directory"

    let writeFile path contents =
        tryWith (fun () -> File.WriteAllText(path, contents)) (sprintf "could not write to %s" path)

    let staticPage templatePath outputPath url =
        result {
            let! template = Templates.newTemplate
            let context = Templates.newContext templatePath
            let! result = Templates.tryRender template context
            let folder = Path.Combine(outputPath, url)
            let path = Path.Combine(folder, "index.html")
            do! createDir folder
            do! writeFile path result
        }

    let postGenerator templatePath outputPath (posts: Post.Post list) =

        // TODO: This and the above in staticPage can be merged.
        let generate (post: Post.Post) =
            result {
                let! template = Templates.newTemplate
                let context = Templates.newContext templatePath
                Templates.addData context "Post" post
                let! result = Templates.tryRender template context
                let folder = Path.Combine(outputPath, post.URL)
                let path = Path.Combine(folder, "index.html")
                do! createDir folder
                do! writeFile path result
            }

        result { return! posts |> List.map generate |> Result.combine |> Result.map ignore }

    let homeGenerator templatePath outputPath (posts: Post.Post list) =
        let firstTen = posts |> List.sortBy (fun it -> it.FrontMatter.Date) |> List.take 3

        result {
            let! template = Templates.newTemplate
            let context = Templates.newContext templatePath
            Templates.addData context "Posts" firstTen
            let! result = Templates.tryRender template context
            let path = Path.Combine(outputPath, "index.html")
            do! writeFile path result
        }
