using FluentAssertions;
using MicroPlumberd.Services.Identity;
using MicroPlumberd.Tests.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace MicroPlumberd.Tests.Unit.Identity;

/// <summary>
/// Plan construction for the identity seed (epic-083 feature-001, acceptance-tests.md "Non-scenario proofs"):
/// declaration order, accumulation across calls, defaults, and the retry backoff sequence.
/// </summary>
[TestCategory("Unit")]
public class IdentitySeedBuilderTests
{
    private static IdentitySeedPlan PlanOf(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        configure(services);
        return services.BuildServiceProvider().GetRequiredService<IdentitySeedPlan>();
    }

    [Fact]
    public void Build_PreservesDeclarationOrder()
    {
        var plan = PlanOf(s => s.AddIdentitySeed(seed => seed
            .Role("Administrator")
            .User("admin@localhost", u => u.WithUserName("admin").WithPassword("Admin123!").InRoles("Administrator"))
            .Role("Accountant")
            .Then((_, _) => Task.CompletedTask)));

        var steps = plan.Build();

        steps.Select(x => x.Label).Should().ContainInOrder(
            "role 'Administrator'",
            "user 'admin@localhost'",
            "role 'Accountant'",
            "custom step #1");
    }

    [Fact]
    public void Build_AccumulatesAcrossSeveralAddIdentitySeedCalls()
    {
        var plan = PlanOf(s =>
        {
            s.AddIdentitySeed(seed => seed.Role("First"));
            s.AddIdentitySeed(seed => seed.Role("Second"));
        });

        plan.Build().Select(x => x.Label).Should().Equal("role 'First'", "role 'Second'");
    }

    [Fact]
    public void Then_DefaultLabelCountsCustomStepsOnly()
    {
        var plan = PlanOf(s => s.AddIdentitySeed(seed => seed
            .Role("A")
            .Then((_, _) => Task.CompletedTask)
            .Role("B")
            .Then((_, _) => Task.CompletedTask)));

        plan.Build().OfType<CustomStep>().Select(x => x.Label)
            .Should().Equal("custom step #1", "custom step #2");
    }

    [Fact]
    public void AddIdentityInitializer_CalledTwice_DoesNotDoubleThePlan()
    {
        var plan = PlanOf(s =>
        {
            s.AddIdentityInitializer(o => o.AdminRoleName = "Owner");
            s.AddIdentityInitializer(o => o.AdminEmail = "root@x");
        });

        var steps = plan.Build();
        steps.OfType<RoleStep>().Should().ContainSingle();
        steps.OfType<UserStep>().Should().ContainSingle();
    }

    [Fact]
    public void Build_RegistersTheHostedRunnerExactlyOnceHoweverManyCalls()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIdentitySeed(seed => seed.Role("First"));
        services.AddIdentitySeed(seed => seed.Role("Second"));
        services.AddIdentityInitializer(o => o.AdminRoleName = "Third");

        services.Count(d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService))
            .Should().Be(1);
    }

    [Fact]
    public void WaitUpTo_DefaultsTo30Seconds()
    {
        var plan = PlanOf(s => s.AddIdentitySeed(seed => seed.Role("Administrator")));
        plan.Build();

        plan.WaitUpTo.Should().Be(TimeSpan.FromSeconds(30));
        IdentitySeedBuilder.DefaultWaitUpTo.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void WaitUpTo_LastCallWins()
    {
        var plan = PlanOf(s =>
        {
            s.AddIdentitySeed(seed => seed.Role("First").WaitUpTo(TimeSpan.FromSeconds(5)));
            s.AddIdentitySeed(seed => seed.Role("Second").WaitUpTo(TimeSpan.FromSeconds(11)));
        });
        plan.Build();

        plan.WaitUpTo.Should().Be(TimeSpan.FromSeconds(11));
    }

    [Fact]
    public void User_WithoutWithUserName_UsesTheEmailAsUserName()
    {
        var plan = PlanOf(s => s.AddIdentitySeed(seed => seed.User("audit@example.com", u => u.InRoles("Auditor"))));

        var step = plan.Build().OfType<UserStep>().Single();
        step.Email.Should().Be("audit@example.com");
        step.UserName.Should().Be("audit@example.com");
        step.Password.Should().BeNull();
        step.Roles.Should().Equal("Auditor");
    }

    [Fact]
    public void User_KeepsUserNamePasswordAndRolesInDeclarationOrder()
    {
        var plan = PlanOf(s => s.AddIdentitySeed(seed => seed.User("admin@localhost", u => u
            .WithUserName("admin")
            .WithPassword("Admin123!")
            .InRoles("Administrator")
            .InRoles("Accountant", "Administrator"))));

        var step = plan.Build().OfType<UserStep>().Single();
        step.UserName.Should().Be("admin");
        step.Password.Should().Be("Admin123!");
        step.Roles.Should().Equal(new[] { "Administrator", "Accountant" },
            "InRoles accumulates in declaration order and de-duplicates");
    }

    [Fact]
    public void AddIdentityInitializer_MapsOptionsOntoTheSeedAtBuildTime()
    {
        var plan = PlanOf(s => s.AddIdentityInitializer(o =>
        {
            o.AdminEmail = "root@x";
            o.AdminUserName = "root";
            o.AdminPassword = "Root123!";
            o.AdminRoleName = "Owner";
            o.ProjectionWaitTime = TimeSpan.FromSeconds(5);
        }));

        var steps = plan.Build();

        steps.OfType<RoleStep>().Single().Name.Should().Be("Owner");
        var user = steps.OfType<UserStep>().Single();
        user.Email.Should().Be("root@x");
        user.UserName.Should().Be("root");
        user.Password.Should().Be("Root123!");
        user.Roles.Should().Equal("Owner");
        plan.WaitUpTo.Should().Be(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void AddIdentityInitializer_WithSeedAdminUserFalse_ContributesNothing()
    {
        var plan = PlanOf(s => s.AddIdentityInitializer(o => o.SeedAdminUser = false));

        plan.Build().Should().BeEmpty();
    }

    [Fact]
    public void Backoff_Is_1_2_5_10_20_30_30_Seconds()
    {
        Enumerable.Range(1, 7)
            .Select(IdentityInitializerService.Backoff)
            .Should().Equal(
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(10),
                TimeSpan.FromSeconds(20),
                TimeSpan.FromSeconds(30),
                TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void WaitUpTo_RejectsANonPositiveBound()
    {
        var builder = new IdentitySeedBuilder();
        builder.Invoking(b => b.WaitUpTo(TimeSpan.Zero)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
