module TodoList.App

open System
open System.IO
open System.Linq
open System.Data
open System.Security.Claims;
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Authentication.Google
open Microsoft.AspNetCore.Authentication.OAuth
open Microsoft.AspNetCore.Authentication
open Giraffe
open Microsoft.Extensions.Configuration
open System.Threading.Tasks
open Npgsql
open User
open Todo

// ---------------------------------
// Web app
// ---------------------------------

let getId (ctx: HttpContext) =
    ctx.User.Claims.Single(fun claim -> claim.Type = ClaimTypes.NameIdentifier).Value |> int

let challengeGoogle: HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let properties = new AuthenticationProperties()
            
            properties.RedirectUri <- "http://localhost:5071/callback"

            do! ctx.ChallengeAsync(GoogleDefaults.AuthenticationScheme, properties)
            
            return! next ctx
        }

let getAllTodoHandler (connection: IDbConnection) =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let id = getId ctx
            let! todos = Todo.getAll connection id

            if Seq.isEmpty todos then
                ctx.SetStatusCode 204
                return! next ctx
            else
                return! json todos next ctx
        }

let getTodoHandler (connection: IDbConnection) (id: int): HttpHandler =
    requiresAuthentication challengeGoogle >=>
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! todo = Todo.get connection id

            match todo with
            | Some todo -> return! json todo next ctx
            | None ->
                ctx.SetStatusCode 404
                return! next ctx
        }

let createTodoHandler (connection: IDbConnection): HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let id = getId ctx            
            let! request = ctx.BindJsonAsync<CreateTodoRequest>()

            let! id = Todo.create connection { UserId = id; Title = request.Title }

            ctx.SetStatusCode 201

            return! text (id.ToString()) next ctx
        }

let updateTodoHandler (connection: IDbConnection) (id: int): HttpHandler =
    requiresAuthentication challengeGoogle >=>
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! request = ctx.BindJsonAsync<UpdateTodoRequest>()

            do! Todo.update connection id { Title = request.Title; CompletedAt = request.CompletedAt }

            ctx.SetStatusCode 204
            
            return! next ctx
        }

let deleteTodoHandler (connection: IDbConnection) (id: int): HttpHandler =
    requiresAuthentication challengeGoogle >=>
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            do! Todo.delete connection id

            ctx.SetStatusCode 204

            return! next ctx
        }

let webApp (connection: IDbConnection) =
    choose [
        GET >=>
            choose [
                route "/sign-in" >=> challengeGoogle
                route "/callback" >=> text "Signed In"
                route "/todos" >=> requiresAuthentication challengeGoogle >=> getAllTodoHandler connection
                routef "/todos/%i" (getTodoHandler connection)
            ]
        POST >=>
            choose [
                route "/todos" >=> requiresAuthentication challengeGoogle >=> createTodoHandler connection
            ]
        PUT >=>
            choose [
                routef "/todos/%i" (updateTodoHandler connection)
            ]
        DELETE >=>
            choose [
                routef "/todos/%i" (deleteTodoHandler connection)
            ]
        setStatusCode 404 >=> text "Not Found" ]

// ---------------------------------
// Error handler
// ---------------------------------

let errorHandler (ex : Exception) (logger : ILogger) =
    logger.LogError(ex, "An unhandled exception has occurred while executing the request.")
    clearResponse >=> setStatusCode 500 >=> text ex.Message

// ---------------------------------
// Config and Main
// ---------------------------------

let configureCors (builder : CorsPolicyBuilder) =
    builder
        .WithOrigins(
            "http://localhost:5000",
            "https://localhost:5001")
       .AllowAnyMethod()
       .AllowAnyHeader()
       |> ignore

let configureApp (connection: IDbConnection) (app : IApplicationBuilder) =
    let env = app.ApplicationServices.GetService<IWebHostEnvironment>()
    (match env.IsDevelopment() with
    | true  ->
        app.UseDeveloperExceptionPage()
    | false ->
        app .UseGiraffeErrorHandler(errorHandler)
            .UseHttpsRedirection())
        .UseCors(configureCors)
        .UseStaticFiles()
        .UseAuthentication()
        .UseGiraffe(webApp connection)

let signInWithGoogle (connection: IDbConnection) (context: OAuthCreatingTicketContext) : Task =
    task {
        let emailAddress = context.Principal.Claims.Single(fun claim -> claim.Type = ClaimTypes.Email).Value;

        let! user = User.getOrCreate connection emailAddress

        let identity = context.Principal.Identity :?> ClaimsIdentity

        let claim = identity.Claims.FirstOrDefault(fun claim -> claim.Type = ClaimTypes.NameIdentifier)

        if (box claim <> null) then
            identity.RemoveClaim(claim)

        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()))
    }

let configureServices (connection: IDbConnection) (services : IServiceCollection) =
    let provider = services.BuildServiceProvider()
    let configuration = provider.GetService<IConfiguration>()

    services.AddAuthentication(fun options ->
        options.DefaultAuthenticateScheme <- CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie()
        .AddGoogle(fun options ->
            options.ClientId <- configuration.GetValue<string>("Authentication:Google:ClientId")
            options.ClientSecret <- configuration.GetValue<string>("Authentication:Google:ClientSecret")
            options.SignInScheme <- CookieAuthenticationDefaults.AuthenticationScheme
            options.Events.OnCreatingTicket <- signInWithGoogle connection)
        |> ignore
    services.AddCors()    |> ignore
    services.AddGiraffe() |> ignore

let configureLogging (builder : ILoggingBuilder) =
    builder.AddConsole()
           .AddDebug() |> ignore

[<EntryPoint>]
let main args =
    let contentRoot = Directory.GetCurrentDirectory()
    let webRoot     = Path.Combine(contentRoot, "WebRoot")

    let configuration = 
        ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional = false, reloadOnChange = true)
            .AddEnvironmentVariables()
            .Build()

    let connectionString = configuration.GetConnectionString("Default")

    let dataSource = NpgsqlDataSource.Create(connectionString);

    let connection = dataSource.CreateConnection();

    Host.CreateDefaultBuilder(args)
        .ConfigureWebHostDefaults(
            fun webHostBuilder ->
                webHostBuilder
                    .UseContentRoot(contentRoot)
                    .UseWebRoot(webRoot)
                    .Configure(configureApp connection)
                    .ConfigureServices(configureServices connection)
                    .ConfigureLogging(configureLogging)
                    |> ignore)
        .Build()
        .Run()
    0