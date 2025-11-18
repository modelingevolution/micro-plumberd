using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Linq;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace MicroPlumberd.SourceGenerators.Tests;

public class GeneratorOutputTests
{
    private readonly ITestOutputHelper _output;

    public GeneratorOutputTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Aggregate_GeneratesCode_PrintOutput()
    {
        var source = @"
using System;
using MicroPlumberd;

namespace TestNamespace
{
    public record FooCreated(string Name);
    public record FooUpdated(int Value);
    public record FooState(string Name, int Value);

    [Aggregate]
    public partial class FooAggregate : AggregateBase<Guid, FooState>
    {
        public FooAggregate(Guid id) : base(id) { }

        private static FooState Given(FooState state, FooCreated ev)
        {
            return state with { Name = ev.Name };
        }

        private static FooState Given(FooState state, FooUpdated ev)
        {
            return state with { Value = ev.Value };
        }
    }
}";

        var generated = RunGenerator<AggregateSourceGenerator>(source);

        _output.WriteLine("========== AGGREGATE GENERATOR OUTPUT ==========");
        _output.WriteLine(generated);
        _output.WriteLine("================================================");

        Assert.Contains("partial class FooAggregate", generated);
        Assert.Contains("IAggregate<FooAggregate>", generated);
    }

    [Fact]
    public void CommandHandler_GeneratesCode_PrintOutput()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using MicroPlumberd.Services;

namespace TestNamespace
{
    public record CreateFoo(string Name);
    public record UpdateFoo(int Value);

    [CommandHandler]
    public partial class FooCommandHandler
    {
        public async Task Handle(string id, CreateFoo cmd)
        {
            await Task.CompletedTask;
        }

        public async Task<string> Handle(string id, UpdateFoo cmd)
        {
            return ""updated"";
        }
    }
}";

        var generated = RunGenerator<CommandHandlerSourceGenerator>(source);

        _output.WriteLine("========== COMMAND HANDLER GENERATOR OUTPUT ==========");
        _output.WriteLine(generated);
        _output.WriteLine("======================================================");

        Assert.Contains("partial class FooCommandHandler", generated);
        Assert.Contains("IServiceTypeRegister", generated);
    }

    [Fact]
    public void EventHandler_GeneratesCode_PrintOutput()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using MicroPlumberd;

namespace TestNamespace
{
    public record FooCreated(string Name);
    public record FooUpdated(int Value);

    [EventHandler]
    public partial class FooEventHandler
    {
        private async Task Given(Metadata m, FooCreated ev)
        {
            await Task.CompletedTask;
        }

        private async Task Given(Metadata m, FooUpdated ev)
        {
            await Task.CompletedTask;
        }
    }
}";

        var generated = RunGenerator<EventHandlerSourceGenerator>(source);

        _output.WriteLine("========== EVENT HANDLER GENERATOR OUTPUT ==========");
        _output.WriteLine(generated);
        _output.WriteLine("====================================================");

        Assert.Contains("partial class FooEventHandler", generated);
        Assert.Contains("IEventHandler", generated);
    }

    [Fact]
    public void ProcessManager_GeneratesCode_PrintOutput()
    {
        var source = @"
using System;
using System.Threading.Tasks;
using MicroPlumberd;
using MicroPlumberd.Services;

namespace TestNamespace
{
    public record OrderCreated(Guid OrderId);
    public record PaymentReceived(Guid OrderId);
    public record ProcessPayment(Guid OrderId);

    [ProcessManager]
    public partial class OrderProcessManager
    {
        private async Task Given(Metadata m, OrderCreated ev)
        {
            await Task.CompletedTask;
        }

        private async Task<ICommandRequest<ProcessPayment>> When(Metadata m, PaymentReceived ev)
        {
            return CommandRequest.Create(new ProcessPayment(ev.OrderId));
        }

        private async Task<ICommandRequest<ProcessPayment>> StartWhen(Metadata m, OrderCreated ev)
        {
            return CommandRequest.Create(new ProcessPayment(ev.OrderId));
        }
    }
}";

        var generated = RunGenerator<ProcessManagerSourceGenerator>(source);

        _output.WriteLine("========== PROCESS MANAGER GENERATOR OUTPUT ==========");
        _output.WriteLine(generated);
        _output.WriteLine("======================================================");

        Assert.Contains("partial class OrderProcessManager", generated);
        Assert.Contains("ProcessManagerBase<Guid>", generated);
        Assert.Contains("IProcessManager", generated);
    }

    private string RunGenerator<T>(string source) where T : IIncrementalGenerator, new()
    {
        // Create compilation
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "TestAssembly",
            new[] { syntaxTree },
            new[]
            {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(Guid).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Threading.Tasks.Task).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Collections.Generic.IEnumerable<>).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location)
            },
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Add generator
        var generator = new T();
        var driver = CSharpGeneratorDriver.Create(generator);

        // Run generator
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        // Get generated source
        var runResult = driver.GetRunResult();

        if (runResult.GeneratedTrees.Length == 0)
        {
            return "No code generated";
        }

        var sb = new StringBuilder();
        foreach (var tree in runResult.GeneratedTrees)
        {
            sb.AppendLine($"// File: {tree.FilePath}");
            sb.AppendLine(tree.ToString());
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
