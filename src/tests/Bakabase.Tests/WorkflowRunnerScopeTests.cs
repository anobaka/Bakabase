using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Components.Telemetry;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.TestKit.Implementations;
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
    [TestMethod]
    public async Task PreflightFailureIsReportedOnlyAfterItIsPersistedAndStaleRunIsNotReportedTwice()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var recorder = new TelemetryRecorder();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBakabaseLocalizer, TestBakabaseLocalizer>();
        services.AddDbContext<TestWorkflowDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton<IWorkflowTriggerRegistry>(new WorkflowTriggerRegistry([]));
        services.AddSingleton<IWorkflowActivityRegistry>(new WorkflowActivityRegistry([]));
        services.AddSingleton<IWorkflowItemTypeRegistry>(new WorkflowItemTypeRegistry([]));
        services.AddSingleton<IProductTelemetry>(recorder);
        services.AddScoped<WorkflowRunner<TestWorkflowDbContext>>();
        await using var root = services.BuildServiceProvider();
        await using var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TestWorkflowDbContext>();
        await db.Database.EnsureCreatedAsync();
        var row = new WorkflowRunDbModel { Status = WorkflowRunStatus.Pending, WorkflowDefinitionId = 9999, StartedAt = DateTime.Now };
        db.Runs.Add(row);
        await db.SaveChangesAsync();
        var runner = scope.ServiceProvider.GetRequiredService<WorkflowRunner<TestWorkflowDbContext>>();
        await runner.ExecuteAsync(row.Id, BuildArgs(root));
        await db.Entry(row).ReloadAsync();
        Assert.AreEqual(WorkflowRunStatus.Failed, row.Status);
        Assert.AreEqual("workflow_run_completed", recorder.Name);
        Assert.AreEqual("failed", recorder.Properties!["outcome"]);
        Assert.AreEqual(1, recorder.Count);
        await runner.ExecuteAsync(row.Id, BuildArgs(root));
        Assert.AreEqual(1, recorder.Count);
    }

    private sealed class TelemetryRecorder : IProductTelemetry
    {
        public int Count;
        public string? Name;
        public System.Collections.Generic.IReadOnlyDictionary<string, object?>? Properties;
        public void Track(string eventName, System.Collections.Generic.IReadOnlyDictionary<string, object?>? properties = null,
            TelemetryActor? actor = null) { Count++; Name = eventName; Properties = properties; }
    }

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
        services.AddSingleton<IBakabaseLocalizer, TestBakabaseLocalizer>();
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
