using CommandLine;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using System.ComponentModel;
using System.Diagnostics;
using Uma.Uuid;
using Solace.ApiServer.Utils;
using Solace.BuildplateImporter;
using Solace.Common;
using Solace.Common.Utils;
using Solace.DB;
using Solace.EventBus.Client;
using Solace.ObjectStore.Client;
using Solace.StaticData;
using SData = Solace.StaticData.StaticData;
using Microsoft.AspNetCore.Authentication;
using Asp.Versioning;
using Solace.ApiServer.Authentication;
using Microsoft.AspNetCore.ResponseCompression;

namespace Solace.ApiServer;

public static class Program
{
    // initialized in main
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    internal static Config config;

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    private sealed class Options
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {
        [Option("port", Default = 80, Required = false, HelpText = "Port to listen on")]
        public int HttpPort { get; set; }

        [Option("earth-db", Default = "./earth.db", Required = false, HelpText = "Earth database connection string")]
        public string EarthDatabaseConnectionString { get; set; }

        [Option("live-db", Default = "./live.db", Required = false, HelpText = "Live database connection string")]
        public string LiveDatabaseConnectionString { get; set; }

        [Option("dir", Default = "./staticdata", Required = false, HelpText = "Static data path")]
        public string StaticDataPath { get; set; }

        [Option("eventbus", Default = "localhost:5532", Required = false, HelpText = "Event bus address")]
        public string EventBusConnectionString { get; set; }

        [Option("objectstore", Default = "localhost:5396", Required = false, HelpText = "Object storage address")]
        public string ObjectStoreConnectionString { get; set; }

        [Option("logger-url", Default = null, Required = false, HelpText = "Url to send logs to")]
        public string? LoggerUrl { get; set; }
    }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

    public static async Task<int> Main(string[] args)
    {
        TypeDescriptor.AddAttributes(typeof(Uuid), new TypeConverterAttribute(typeof(StringToUuidConv)));

        Environment.CurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;

        /*var log = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/api_server/log.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
            .MinimumLevel.Override("Solace.ApiServer.Authentication", LogEventLevel.Warning)
            .CreateLogger();*/

        if (!Debugger.IsAttached)
        {
            AppDomain.CurrentDomain.UnhandledException += (object sender, UnhandledExceptionEventArgs e) =>
            {
                Log.Fatal($"Unhandeled exception: {e.ExceptionObject}");
                Log.CloseAndFlush();
                Environment.Exit(1);
            };
        }

        ParserResult<Options> res = Parser.Default.ParseArguments<Options>(args);

        Options options;
        if (res is Parsed<Options> parsed)
        {
            options = parsed.Value;
        }
        else if (res is NotParsed<Options> notParsed)
        {
            if (res.Errors.Any(error => error is HelpRequestedError))
            {
                return 0;
            }
            else if (res.Errors.Any(error => error is VersionRequestedError))
            {
                return 0;
            }
            else
            {
                return 1;
            }
        }
        else
        {
            return 1;
        }

        var loggerConfig = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/api_server/log.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .Enrich.WithProperty("ComponentName", "ApiServer");

        if (!string.IsNullOrWhiteSpace(options.LoggerUrl))
        {
            loggerConfig.WriteTo.Http(options.LoggerUrl, 10 * 1024 * 1024);
        }

        loggerConfig
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Information)
            .MinimumLevel.Override("Solace.ApiServer.Authentication", LogEventLevel.Information);
        var log = loggerConfig.CreateLogger();

        Log.Logger = log;

        Log.Information("Loading configuration");
        try
        {
            const string configFileName = "api_config.json";
            if (!File.Exists(configFileName))
            {
                config = Config.Default;
                File.WriteAllText(configFileName, Json.SerializeIndented(config));
                Log.Information($"Configuration file not found or invalid, created with default values: {Path.GetFullPath(configFileName)}");
            }
            else
            {
                config = Json.Deserialize<Config>(File.ReadAllText(configFileName)) ?? Config.Default;
            }
        }
        catch (Exception ex)
        {
            Log.Fatal($"Failed to load configuration: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Loaded configuration");

        Log.Information("Connecting to event bus");
        EventBusClient eventBus;
        try
        {
            eventBus = await EventBusClient.ConnectAsync(options.EventBusConnectionString);
        }
        catch (EventBusClientException ex)
        {
            Log.Fatal($"Could not connect to event bus: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Connected to event bus");
        Log.Information("Connecting to object storage");
        ObjectStoreClient objectStore;
        try
        {
            objectStore = await ObjectStoreClient.ConnectAsync(options.ObjectStoreConnectionString);
        }
        catch (ObjectStoreClientException ex)
        {
            Log.Fatal($"Could not connect to object storage: {ex}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Connected to object storage");

        Log.Information("Loading static data");
        SData staticData;
        try
        {
            staticData = new SData(options.StaticDataPath);
        }
        catch (StaticDataException staticDataException)
        {
            Log.Fatal($"Failed to load static data: {staticDataException}");
            Log.CloseAndFlush();
            return 1;
        }

        Log.Information("Loaded static data");

        Log.Information("Importing shop buildplates");

        string earthDbConnectionString = "Data Source=" + options.EarthDatabaseConnectionString!;
        var earthDbOptionsBuilder = new DbContextOptionsBuilder<EarthDbContext>();
        earthDbOptionsBuilder.UseSqlite(earthDbConnectionString);

        using (var earthDbContext = new EarthDbContext(earthDbOptionsBuilder.Options))
        {
            var currentShopBuildplates = await earthDbContext.TemplateBuildplates
                .AsNoTracking()
                .ToListAsync();

            var importer = new Importer(earthDbContext, eventBus, objectStore, Log.Logger);
            foreach (var buidplate in staticData.Buildplates.ShopBuildplates)
            {
                if (earthDbContext.TemplateBuildplates.Any(bp => bp.Id == buidplate.Id))
                {
                    Log.Debug($"Shop buildplate {buidplate.Id} already exists");
                    continue;
                }

                try
                {
                    Log.Information($"Importing shop buildplate {buidplate.Id}");

                    string name = "unknown buildplate";
                    var bpPlayfabItem = staticData.Playfab.Items.Values.FirstOrDefault(item => item.Data is Playfab.Item.BuildplateData bpData && bpData.Id == buidplate.Id);
                    if (bpPlayfabItem is not null)
                    {
                        name = bpPlayfabItem.Title;
                    }

                    using (var buidplateData = buidplate.OpenRead())
                    {
                        await importer.ImportTemplateAsync(buidplate.Id, $"[SHOP] {name}", buidplateData);
                    }
                }
                catch (Exception ex)
                {
                    Log.Fatal($"Failed to import shop buidplate {buidplate.Id}: {ex}");
                    Log.CloseAndFlush();
                    return 1;
                }
            }
        }

        Log.Information("Imported shop buidplates");

        var tappablesManager = await TappablesManager.CreateAsync(eventBus);
        var buildplateInstancesManager = await BuildplateInstancesManager.CreateAsync(eventBus);

        using var birhEarthDb = new EarthDbContext(earthDbOptionsBuilder.Options);
        BuildplateInstanceRequestHandler.Start(birhEarthDb, eventBus, objectStore, staticData.Catalog, buildplateInstancesManager);

        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog();

        builder.WebHost.UseUrls($"http://*:{options.HttpPort}/");

        builder.Services.AddSingleton(eventBus);
        builder.Services.AddSingleton(objectStore);
        builder.Services.AddSingleton(staticData);
        builder.Services.AddSingleton(tappablesManager);
        builder.Services.AddSingleton(buildplateInstancesManager);

        builder.Services.AddControllers()
           .ConfigureApplicationPartManager(manager =>
           {
               manager.FeatureProviders.Add(new InternalControllerFeatureProvider());
           });

        builder.Services.AddResponseCompression(options =>
        {
            options.Providers.Add<GzipCompressionProvider>();
        });

        builder.Services.AddResponseCaching();

        builder.Services.AddApiVersioning(config =>
        {
            config.DefaultApiVersion = new ApiVersion(1, 1);
            config.AssumeDefaultVersionWhenUnspecified = true;
            config.ReportApiVersions = true;
        });

        builder.Services.AddAuthentication("GenoaAuth")
            .AddScheme<AuthenticationSchemeOptions, GenoaAuthenticationHandler>("GenoaAuth", null);

        builder.Services.AddDbContext<EarthDbContext>(options => options.UseSqlite(earthDbConnectionString));

        var app = builder.Build();

        app.Use(async (context, next) =>
       {
           context.Items.Add(RequestUtils.TimestampKey, DateTimeOffset.UtcNow);
           await next();
       });

        app.UseSerilogRequestLogging(options =>
        {
            // Customize the message template
            options.MessageTemplate = "{RemoteIpAddress} {RequestMethod} {RequestScheme}://{RequestHost}{RequestPath}{RequestQuery} responded {StatusCode} in {Elapsed:0.0000} ms";

            // Emit debug-level events instead of the defaults
            options.GetLevel = (httpContext, elapsed, ex) => LogEventLevel.Verbose;

            // Attach additional properties to the request completion event
            options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                diagnosticContext.Set("RemoteIpAddress", httpContext.Connection.RemoteIpAddress);
                diagnosticContext.Set("RequestQuery", httpContext.Request.QueryString);
            };
        });

        app.UseETagger();
        //app.UseHttpsRedirection();

        app.UseRouting();

        app.UseStaticFiles();

        app.UseAuthentication();
        app.UseAuthorization();

        //app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TransactionManager.MaximumTimeout });

        app.UseResponseCaching();

        app.UseResponseCompression();

        //app.UseSession();

        app.MapControllers();

        await app.RunAsync();

        return 0;
    }
}
