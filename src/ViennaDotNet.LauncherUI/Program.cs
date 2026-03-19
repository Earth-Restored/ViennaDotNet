using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Events;
using ViennaDotNet.LauncherUI.Components;
using ViennaDotNet.LauncherUI.Components.Account;
using ViennaDotNet.LauncherUI.Data;

namespace ViennaDotNet.LauncherUI;

public partial class Program
{
    public const string ProgramsDir = "./"; // same as launcher
    public const string StaticDataDir = "staticdata";

    public static string Address { get; private set; } = "";

    public static string LoggerAddress => Address + "/api/logs/create";

    private static async Task Main(string[] args)
    {
        Environment.CurrentDirectory = AppContext.BaseDirectory; // todo:

        Settings.Instance = await Settings.LoadAsync(Settings.DefaultPath);

        var builder = WebApplication.CreateBuilder(args);

        var logsLogService = new LogsLogService();
        builder.Services.AddSingleton(logsLogService);

        var log = new LoggerConfiguration()
            .WriteTo.Console()
            .WriteTo.File("logs/launcher/log.txt", rollingInterval: RollingInterval.Day, rollOnFileSizeLimit: true, fileSizeLimitBytes: 8338607, outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.LogsLogSink(logsLogService)
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Information)
            .MinimumLevel.Override("ViennaDotNet.ApiServer.Authentication", LogEventLevel.Information)
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

        builder.Services.AddControllers();

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

            Address = addressFeature?.Addresses.FirstOrDefault() ?? "";
        });

        // Apply database migrations and initialize built-in roles
        using (var scope = app.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.Database.MigrateAsync();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            await EnsureBuiltInRolesAsync(roleManager);
        }

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
            if (policy != null) return policy;

            return new AuthorizationPolicyBuilder()
                .AddRequirements(new PermissionRequirement(policyName))
                .Build();
        }
    }
}