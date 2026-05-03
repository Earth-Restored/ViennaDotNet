using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using Solace.DB;
using Solace.LauncherUI.Components;
using Solace.LauncherUI.Components.Account;
using Solace.LauncherUI.Data;
using Solace.LauncherUI.Utils;
using Solace.ObjectStore.Client;

namespace Solace.LauncherUI;

public partial class Program
{
    public static readonly string ProgramsDir = Path.GetFullPath("./../components");
    public static readonly string StaticDataDir = Path.GetFullPath(Path.Combine("..", "staticdata"));
    public static readonly string DataDirRelative = Path.Combine("..", "data");
    public static readonly string DataDir = Path.GetFullPath(DataDirRelative);

    public static string Address { get; private set; } = "";

    public static string LoggerAddress => Address + "/api/logs/create";

    private static async Task Main(string[] args)
    {
        // Environment.CurrentDirectory = AppContext.BaseDirectory; // todo:

        Settings.Instance = await Settings.LoadAsync(Settings.DefaultPath);

        var builder = WebApplication.CreateBuilder(args);

        var logsLogService = new LogsLogService();
        builder.Services.AddSingleton(logsLogService);

        var log = new LoggerConfiguration()
            .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.File("logs/launcher/log.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}", formatProvider: CultureInfo.InvariantCulture)
            .WriteTo.LogsLogSink(logsLogService)
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Information)
            .MinimumLevel.Override("Solace.ApiServer.Authentication", LogEventLevel.Information)
            .CreateLogger();

        Log.Logger = log;

        builder.Services.AddSingleton<ServerManager>();

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<IdentityRedirectManager>();
        builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddIdentityCookies();

        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            options.UseSqlite(connectionString));
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(connectionString));
        builder.Services.AddDatabaseDeveloperPageExceptionFilter();

        builder.Services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.SignIn.RequireConfirmedAccount = true;
                options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

        builder.Services.AddControllers()
            .ConfigureApplicationPartManager(manager =>
            {
                manager.FeatureProviders.Add(new InternalControllerFeatureProvider());
            });

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            app.UseMigrationsEndPoint();
        }
        else
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        app.UseHttpsRedirection();

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        // Add additional endpoints required by the Identity /Account Razor components.
        app.MapAdditionalIdentityEndpoints();

        app.MapControllers();

        app.Lifetime.ApplicationStarted.Register(() =>
        {
            var server = app.Services.GetRequiredService<IServer>();
            var addressFeature = server.Features.Get<IServerAddressesFeature>();

            var address = (addressFeature?.Addresses.FirstOrDefault() ?? "").AsSpan();
            var index = address.IndexOf("://");
            if (index != -1)
            {
                address = address[(index + 3)..];
            }

            if (IPEndPoint.TryParse(address, out var endpoint))
            {
                Address = $"http://localhost:{endpoint.Port}";
            }
            else
            {
                Address = "http://localhost:5000";
            }
        });

        // Apply database migrations and initialize built-in roles
        using (var scope = app.Services.CreateScope())
        {
            // make sure Data dir exists
            Directory.CreateDirectory(Path.Combine(Directory.GetCurrentDirectory(), "Data"));

            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.MigrateAsync();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            await EnsureBuiltInRolesAsync(roleManager);
        }

        // extract buildplates from db/objectstore
        // var sm = app.Services.GetService<ServerManager>()!;
        // await sm.EnsureComponentsOnline(Programs.ObjectStoreServer.ExeName);

        // using var earthDB = EarthDB.Open(Settings.Instance.EarthDatabaseConnectionString ?? "");
        // await using var objectStore = await ObjectStoreClient.ConnectAsync("localhost:" + Settings.Instance.ObjectStorePort);

        // var results = await new EarthDB.ObjectQuery(false)
        //     .SearchBuildplates(out var searchArguments, true, true)
        //     .ExecuteAsync(earthDB, default);

        // var (buildplates, buildplatesCount, totalCount) = results.GetBuildplates(searchArguments);

        // Directory.CreateDirectory("bps");

        // foreach (var (buildplateId, dbBuildplate) in buildplates)
        // {
        //     var preview64 = await objectStore.GetAsync(dbBuildplate!.PreviewObjectId);

        //     var preview = Convert.FromBase64String(Encoding.UTF8.GetString(preview64!));

        //     File.WriteAllBytes(Path.Combine("bps", dbBuildplate.Name[7..]), preview);
        // }

        app.Run();
    }

    private static async Task EnsureBuiltInRolesAsync(RoleManager<ApplicationRole> roleManager)
    {
        var ownerRole = await roleManager.FindByNameAsync(ApplicationRole.Owner);

        if (ownerRole == null)
        {
            ownerRole = new ApplicationRole
            {
                Name = ApplicationRole.Owner,
                Position = 0,
                Color = "#FF0000",
                IsBuiltIn = true
            };
            await roleManager.CreateAsync(ownerRole);
        }

        // Sync Permissions
        var currentClaims = await roleManager.GetClaimsAsync(ownerRole);
        var currentPermissionValues = currentClaims
            .Where(c => c.Type == "Permission")
            .Select(c => c.Value)
            .ToHashSet();

        foreach (var permission in Permissions.All)
        {
            if (!currentPermissionValues.Contains(permission))
            {
                // Add the missing permission
                await roleManager.AddClaimAsync(ownerRole, new Claim("Permission", permission));
            }
        }

        // Remove permissions from the Owner that no longer exist in the code
        foreach (var claim in currentClaims.Where(c => c.Type == "Permission"))
        {
            if (!Permissions.All.Contains(claim.Value))
            {
                await roleManager.RemoveClaimAsync(ownerRole, claim);
            }
        }
    }

    private sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
        : DefaultAuthorizationPolicyProvider(options)
    {
        public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
        {
            var policy = await base.GetPolicyAsync(policyName);
            if (policy != null)
            {
                return policy;
            }

            return new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(policyName))
                .Build();
        }
    }
}