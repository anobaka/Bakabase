using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Acquisition.Components;

/// <summary>
/// Puts startup recipes in the database once per built-in name and trigger. A user-created
/// definition with the same name remains independent and does not suppress the built-in.
/// <para>
/// A recipe whose steps are not all implemented yet is skipped rather than created broken — the
/// steps arrive over several releases, and a definition naming an activity that does not exist
/// cannot be saved, let alone run. Each release therefore seeds a little more, and a user who was
/// there for the earlier ones gets the new recipes on the next start.
/// </para>
/// <para>
/// Existing recipes are never rewritten. A seed that overwrote itself would silently discard a
/// user's copy-and-edit; the built-in flag makes them read-only instead, and changing one means
/// copying it.
/// </para>
/// </summary>
public class AcquisitionRecipeSeeder<TDbContext>(
    TDbContext db,
    IWorkflowDefinitionService workflows,
    IWorkflowActivityRegistry activities,
    ILogger<AcquisitionRecipeSeeder<TDbContext>> logger)
    where TDbContext : DbContext
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var existing = await db.Set<WorkflowDefinitionDbModel>()
            .Where(d => d.IsBuiltin && d.TriggerKind == AcquisitionWorkflowKinds.TriggerRequested)
            .Select(d => d.Name)
            .ToListAsync(ct);
        var known = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seeded = 0;

        foreach (var recipe in BuiltinAcquisitionRecipes.All)
        {
            if (known.Contains(recipe.Name))
            {
                // Enrich shipped documentation only. Never replace an existing node chain:
                // suspended runs persist a cursor into it, and copies belong to their users.
                await db.Set<WorkflowDefinitionDbModel>()
                    .Where(d => d.TriggerKind == AcquisitionWorkflowKinds.TriggerRequested &&
                                d.Name == recipe.Name && d.IsBuiltin && d.DescriptionKey == null &&
                                (d.Description == null || d.Description == ""))
                    .ExecuteUpdateAsync(s => s.SetProperty(d => d.Description, recipe.Description)
                        .SetProperty(d => d.DescriptionKey, recipe.DescriptionKey), ct);
                continue;
            }

            // Template-only recipes remain available by name for existing configured defaults,
            // but new installations do not acquire unused definitions. Never rewrite old runs.
            if (!recipe.SeedOnStartup) continue;

            var missing = recipe.Steps
                .Select(s => s.Kind)
                .Where(kind => !activities.TryGet(kind, out _))
                .ToList();

            if (missing.Count > 0)
            {
                logger.LogDebug(
                    "[Acquisition] Recipe \"{Name}\" not seeded yet — it needs {Steps}",
                    recipe.Name, string.Join(", ", missing));

                continue;
            }

            var created = await workflows.CreateAsync(new WorkflowDefinitionCreationInputModel
            {
                Name = recipe.Name,
                Description = recipe.Description,
                DescriptionKey = recipe.DescriptionKey,
                TriggerKind = AcquisitionWorkflowKinds.TriggerRequested,
                Enabled = true,
                Activities = recipe.Steps.Select(s => new WorkflowActivityInputModel
                {
                    Kind = s.Kind,
                    ConfigJson = s.ConfigJson ?? "{}",
                    // A step that fails stops the acquisition: the next step would work on files
                    // that were never fetched.
                    OnItemError = WorkflowActivityErrorBehavior.Fail,
                }).ToList()
            }, ct);

            await db.Set<WorkflowDefinitionDbModel>()
                .Where(d => d.Id == created.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsBuiltin, _ => true), ct);

            seeded++;
        }

        if (seeded > 0)
            logger.LogInformation("[Acquisition] Seeded {Count} built-in recipes", seeded);
    }
}
