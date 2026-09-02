using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Components;
using Bootstrap.Components.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// Every workflow run is enqueued as a BTask and executes long after the scope that built the
/// runner has been disposed. The runner therefore must not create its scopes from the provider that
/// resolved it.
/// </summary>
[TestClass]
public class WorkflowRunnerScopeTests
{
    private sealed class TestWorkflowDbContext(DbContextOptions<TestWorkflowDbContext> options)
        : DbContext(options)
    {
        public DbSet<WorkflowDefinitionDbModel> Definitions { get; set; } = null!;
        public DbSet<WorkflowRunDbModel> Runs { get; set; } = null!;
        public DbSet<WorkflowActivityDbModel> Activities { get; set; } = null!;
    }

    private static BTaskArgs BuildArgs(IServiceProvider sp) => new(
        new PauseToken(),
        CancellationToken.None,
        new BTask("test", () => "test"),
        _ => Task.CompletedTask,
        sp);

    [TestMethod]
    public async Task ExecuteAsync_StillWorksAfterTheResolvingScopeIsDisposed()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();

        services.AddLogging();
        services.AddDbContext<TestWorkflowDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton<IWorkflowTriggerRegistry>(new WorkflowTriggerRegistry([]));
        services.AddSingleton<IWorkflowActivityRegistry>(new WorkflowActivityRegistry([]));
        services.AddSingleton<IWorkflowItemTypeRegistry>(new WorkflowItemTypeRegistry([]));
        services.AddSingleton(NullLogger<WorkflowRunner<TestWorkflowDbContext>>.Instance);
        services.AddScoped<WorkflowRunner<TestWorkflowDbContext>>();

        await using var root = services.BuildServiceProvider();

        await using (var setup = root.CreateAsyncScope())
        {
            await setup.ServiceProvider.GetRequiredService<TestWorkflowDbContext>()
                .Database.EnsureCreatedAsync();
        }

        WorkflowRunner<TestWorkflowDbContext> runner;

        // Mirrors the real call sites: the runner is resolved in a scope, a BTask closes over it,
        // and the scope goes away before that task ever runs.
        await using (var scope = root.CreateAsyncScope())
        {
            runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner<TestWorkflowDbContext>>();
        }

        // Used to throw ObjectDisposedException here, failing every run re-enqueued at startup.
        // A missing run id is a no-op, which is all this needs to prove the scope is usable.
        await runner.ExecuteAsync(9999, BuildArgs(root));
    }
}
