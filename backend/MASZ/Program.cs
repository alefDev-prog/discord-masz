using AspNetCoreRateLimit;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using MASZ.Data;
using MASZ.Logger;
using MASZ.Middlewares;
using MASZ.Plugins;
using MASZ.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();

builder.Logging.AddProvider(new LoggerProvider());

builder.WebHost.UseUrls("http://0.0.0.0:80/");

string connectionString =
            $"Server={   Environment.GetEnvironmentVariable("MYSQL_HOST")};" +
            $"Port={     Environment.GetEnvironmentVariable("MYSQL_PORT")};" +
            $"Database={ Environment.GetEnvironmentVariable("MYSQL_DATABASE")};" +
            $"Uid={      Environment.GetEnvironmentVariable("MYSQL_USER")};" +
            $"Pwd={      Environment.GetEnvironmentVariable("MYSQL_PASSWORD")};";

builder.Services.AddDbContext<DataContext>(x => x.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddControllers()
    .AddNewtonsoftJson(x => x.SerializerSettings.ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore);

builder.Services.AddSingleton(provider =>
{
    var client = new DiscordSocketClient(new DiscordSocketConfig
    {
        AlwaysDownloadUsers = true,
        MessageCacheSize = 50,
        LogLevel = LogSeverity.Debug
    });

    return client;
})

.AddSingleton(new InteractionServiceConfig
{
    DefaultRunMode = RunMode.Async,
    LogLevel = LogSeverity.Debug,
    UseCompiledLambda = true
})

.AddSingleton<InteractionService>()

.AddScoped<Database>()

.AddScoped<Translator>()

.AddScoped<NotificationEmbedCreator>()

.AddScoped<DiscordAnnouncer>()

.AddSingleton<FilesHandler>()

.AddSingleton<InternalConfiguration>()

.AddSingleton<DiscordBot>()

.AddSingleton<DiscordAPIInterface>()

.AddSingleton<IdentityManager>()

.AddSingleton<PunishmentHandler>()

.AddSingleton<DiscordEventHandler>()

.AddSingleton<Scheduler>()

.AddSingleton<AuditLogger>();


// Plugin
// ######################################################################################################

if (string.Equals("true", Environment.GetEnvironmentVariable("ENABLE_CUSTOM_PLUGINS")))
{
    Console.WriteLine("########################################################################################################");
    Console.WriteLine("ENABLED CUSTOM PLUGINS!");
    Console.WriteLine("This might impact the performance or security of your MASZ instance!");
    Console.WriteLine("Use this only if you know what you are doing!");
    Console.WriteLine("For support and more information, refer to the creator or community of your plugin!");
    Console.WriteLine("########################################################################################################");

    builder.Services.Scan(scan => scan
        .FromAssemblyOf<IBasePlugin>()
        .AddClasses(classes => classes.InNamespaces("MASZ.Plugins"))
        .AsImplementedInterfaces()
        .WithSingletonLifetime());
}
// ######################################################################################################

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie("Cookies", options =>
    {
        options.LoginPath = "/api/v1/login";
        options.LogoutPath = "/api/v1/logout";
        options.ExpireTimeSpan = new TimeSpan(7, 0, 0, 0);
        options.Cookie.MaxAge = new TimeSpan(7, 0, 0, 0);
        options.Cookie.Name = "masz_access_token";
        options.Cookie.HttpOnly = false;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.Headers["Location"] = context.RedirectUri;
            context.Response.StatusCode = 401;
            return Task.CompletedTask;
        };
    })
    .AddDiscord(options =>
    {
        options.ClientId = Environment.GetEnvironmentVariable("DISCORD_OAUTH_CLIENT_ID");
        options.ClientSecret = Environment.GetEnvironmentVariable("DISCORD_OAUTH_CLIENT_SECRET");
        options.Scope.Add("guilds");
        options.Scope.Add("identify");
        options.SaveTokens = true;
        options.Prompt = "none";
        options.AccessDeniedPath = "/oauthfailed";
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.HttpOnly = false;
    });


builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer("Tokens", x =>
    {
        x.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(
                    Environment.GetEnvironmentVariable("DISCORD_BOT_TOKEN"))),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.DefaultPolicy = new AuthorizationPolicyBuilder("Cookies", "Tokens")
        .RequireAuthenticatedUser()
        .Build();
});

if (string.Equals("true", Environment.GetEnvironmentVariable("ENABLE_CORS")))
{
    builder.Services.AddCors(o => o.AddPolicy("AngularDevCors", builder =>
    {
        builder.WithOrigins("http://127.0.0.1:4200")
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    }));
}

// Needed to store rate limit counters and ip rules
builder.Services.AddMemoryCache();

// Load general configuration from appsettings.json
builder.Services.Configure<IpRateLimitOptions>(builder.Configuration.GetSection("IpRateLimiting"));

// Load ip rules from appsettings.json
builder.Services.Configure<IpRateLimitPolicies>(builder.Configuration.GetSection("IpRateLimitPolicies"));

// Inject counter and rules stores
builder.Services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
builder.Services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();

builder.Services.AddMvc();

// https://github.com/aspnet/Hosting/issues/793
// the IHttpContextAccessor service is not registered by default.
// the clientId/clientIp resolvers use it.
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

// Configuration (resolvers, counter key builders)
builder.Services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseMiddleware<HeaderMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<APIExceptionHandlingMiddleware>();

if (string.Equals("true", Environment.GetEnvironmentVariable("ENABLE_CORS")))
{
    app.UseCors("AngularDevCors");
}

app.UseIpRateLimiting();

using (var scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
{
    scope.ServiceProvider.GetRequiredService<DataContext>().Database.Migrate();
}

using (var scope = app.Services.GetRequiredService<IServiceScopeFactory>().CreateScope())
{
    scope.ServiceProvider.GetRequiredService<InternalConfiguration>().Init();

    await scope.ServiceProvider.GetRequiredService<AuditLogger>().Startup();
    await scope.ServiceProvider.GetRequiredService<PunishmentHandler>().StartTimers();
    await scope.ServiceProvider.GetRequiredService<Scheduler>().StartTimers();

    await scope.ServiceProvider.GetRequiredService<DiscordBot>().Start();

    if (string.Equals("true", Environment.GetEnvironmentVariable("ENABLE_CUSTOM_PLUGINS")))
        scope.ServiceProvider.GetServices<IBasePlugin>().ToList().ForEach(x => x.Init());

    scope.ServiceProvider.GetRequiredService<AuditLogger>().RegisterEvents();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
});