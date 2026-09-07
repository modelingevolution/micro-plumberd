using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

using MudBlazor.Services;
using MicroPluberd.Examples.Blazor.Identity2.Components;
using MicroPluberd.Examples.Blazor.Identity2.Components.Account;

using MicroPlumberd.Services;
using MicroPlumberd.Services.Identity;
using KurrentDB.Client;
using MicroPlumberd;
using System.Security.Claims;
using MicroPluberd.Examples.Blazor.Identity2.Components.SampleLogic;
using MicroPlumberd.Services.Cron;

namespace MicroPluberd.Examples.Blazor.Identity2;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Load secrets from appsettings.secret.json (not committed to git)
        builder.Configuration.AddJsonFile("appsettings.secret.json", optional: true, reloadOnChange: true);

        // Add MudBlazor services
        builder.Services.AddMudServices();

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<IdentityUserAccessor>();
        builder.Services.AddScoped<IdentityRedirectManager>();
        builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        builder.Services.AddAuthentication(options =>
            {
                options.DefaultScheme = IdentityConstants.ApplicationScheme;
                options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
            })
            .AddGoogle(options =>
            {
                options.ClientId = builder.Configuration["Authentication:Google:ClientId"]
                    ?? throw new InvalidOperationException("Google ClientId not configured");
                options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]
                    ?? throw new InvalidOperationException("Google ClientSecret not configured");
            })
            .AddIdentityCookies();

       
        
        var connection = await GetEventStoreSettings(builder.Configuration);
        builder.Services.AddPlumberd(connection, ConfigurePlumberd, true)
            .AddCron()
            .AddScopedCommandHandler<StartWorkflowHandler>()
            .AddSingletonCommandHandler<CompleteWorkflowHandler>();

        builder.Services.AddPlumberdIdentity(async (sp,flow) =>
        {
            if (flow == Flow.Component)
            {
                var p = await sp.GetRequiredService<AuthenticationStateProvider>().GetAuthenticationStateAsync();
                return p?.User?.FindFirstValue(ClaimTypes.NameIdentifier)!;
            }

            return null;
        });

        // Declare the identity state that must exist. The seed runs once the identity read models are live,
        // converges the store to this declaration, and never stops the host.
        builder.Services.AddIdentitySeed(seed => seed
            .Role("Administrator")
            .User(builder.Configuration["Identity:AdminEmail"] ?? "admin@localhost", u => u
                .WithUserName(builder.Configuration["Identity:AdminUserName"] ?? "admin")
                .WithPassword(builder.Configuration["Identity:AdminPassword"] ?? "Admin123!")
                .InRoles("Administrator"))
            .WaitUpTo(TimeSpan.FromSeconds(30)));

        // Opt-in readiness entry: "identity" is Unhealthy (naming the step or the last error) until the seed
        // converged, Healthy afterwards.
        builder.Services.AddHealthChecks()
            .AddPlumberdHealthChecks()
            .AddIdentitySeedHealthCheck();

        // A stated choice, never the silent default (requirement R8). The .NET default is StopHost: one throwing
        // background service takes the whole host down, which in a container is a restart loop. The identity seed
        // never throws out of ExecuteAsync, but other hosted services might, so the host says what it wants.
        builder.Services.Configure<HostOptions>(o =>
            o.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore);

        builder.Services.AddSingleton<IEmailSender<User>, IdentityNoOpEmailSender>();

        var app = builder.Build();

        // Configure the HTTP request pipeline.
        if (app.Environment.IsDevelopment())
        {
            
        }
        else
        {
            app.UseExceptionHandler("/Error");
        }

        app.UseAntiforgery();

        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.MapHealthChecks("/health");

        // Add additional endpoints required by the Identity /Account Razor components.
        app.MapAdditionalIdentityEndpoints();

        app.Run();
    }
    private static void ConfigurePlumberd(IServiceProvider sp, IPlumberConfig x)
    {

    }

    private static async Task<KurrentDBClientSettings> GetEventStoreSettings(IConfiguration config)
    {
        
        var connectionString = config.GetValue<string>("EventStore");
        var conn = KurrentDBClientSettings.Create(connectionString!);
        await conn.WaitUntilReady(TimeSpan.FromSeconds(120));
        return conn;
    }
}
