using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;

using MudBlazor.Services;
using MicroPluberd.Examples.Blazor.Identity2.Components;
using MicroPluberd.Examples.Blazor.Identity2.Components.Account;

using MicroPlumberd.Services;
using MicroPlumberd.Services.Identity;
using EventStore.Client;
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

        // Add MudBlazor services
        builder.Services.AddMudServices();

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddCascadingAuthenticationState();
        builder.Services.AddScoped<IdentityUserAccessor>();
        builder.Services.AddScoped<IdentityRedirectManager>();
        builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

        //builder.Services.AddAuthentication(options =>
        //    {
        //        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        //        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
        //    })
        //    .AddIdentityCookies();

       
        
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

        // Add additional endpoints required by the Identity /Account Razor components.
        app.MapAdditionalIdentityEndpoints();

        app.Run();
    }
    private static void ConfigurePlumberd(IServiceProvider sp, IPlumberConfig x)
    {

    }

    private static async Task<EventStoreClientSettings> GetEventStoreSettings(IConfiguration config)
    {
        
        var connectionString = config.GetValue<string>("EventStore");
        var conn = EventStoreClientSettings.Create(connectionString!);
        await conn.WaitUntilReady(TimeSpan.FromSeconds(120));
        return conn;
    }
}
