using Asp.Versioning;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.ResponseCompression;
using Serilog;
using Serilog.Events;
using System.Transactions;
using ViennaDotNet.Authentication;

namespace ViennaDotNet
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            //services.AddRazorPages();
            services.AddControllers();

            services.AddResponseCompression(options =>
            {
                options.Providers.Add<GzipCompressionProvider>();
            });

            services.AddResponseCaching();

            services.AddApiVersioning(config =>
            {
                config.DefaultApiVersion = new ApiVersion(1, 1);
                config.AssumeDefaultVersionWhenUnspecified = true;
                config.ReportApiVersions = true;
            });

            services.AddAuthentication("GenoaAuth")
                .AddScheme<AuthenticationSchemeOptions, GenoaAuthenticationHandler>("GenoaAuth", null);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
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

            app.UseRouting();

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.UseAuthorization();

            //app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TransactionManager.MaximumTimeout });

            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }
    }
}
