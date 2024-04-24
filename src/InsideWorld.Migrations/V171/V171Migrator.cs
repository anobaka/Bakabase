using Bakabase.Infrastructures.Components.App.Migrations;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Player;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Extensions;
using InsideWorld.Migrations.Legacies;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Threading.Channels;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Business.Models.Dto;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Aos;
using InsideWorld.Migrations.V171.Legacy.Models;

namespace InsideWorld.Migrations.V171
{
    internal sealed class V171Migrator : AbstractMigrator
    {
        public V171Migrator(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        protected override string MaxVersionString => "1.7.0";

        protected override async Task MigrateAfterDbMigrationInternal(object context)
        {
	        try
	        {
		        var mlService = GetRequiredService<MediaLibraryService>();
		        var mls = await mlService.GetAll();
		        foreach (var ml in mls)
		        {
			        var changed = false;
			        if (!string.IsNullOrEmpty(ml.PathConfigurationsJson))
			        {
				        var pcs =
					        JsonConvert.DeserializeObject<CompatiblePathConfiguration[]>(ml.PathConfigurationsJson)!;
				        foreach (var pc in pcs)
				        {
					        if (pc is {Segments: not null, RpmValues: null})
					        {
						        pc.RpmValues = pc.Segments.Select(s => new MatcherValue
						        {
							        Key = s.Key,
							        Layer = s.IsReverse ? -s.Layer : s.Layer,
							        Regex = s.Regex,
							        Property = s.Type,
							        // Previous matchers are always matching by layer.
							        ValueType = ResourceMatcherValueType.Layer
						        }).ToList();
						        pc.Segments = null;
						        changed = true;
					        }

					        pc.RpmValues ??= new List<MatcherValue>();

					        if (pc.Regex.IsNotEmpty())
					        {
						        if (pc.RpmValues.All(m => m.Property != ResourceProperty.Resource))
						        {
							        var matcher = new MatcherValue()
							        {
								        Property = ResourceProperty.Resource,
							        };
							        if (pc.Regex?.TryGetLayer(out var layer) == true)
							        {
								        matcher.Layer = layer;
								        matcher.ValueType = ResourceMatcherValueType.Layer;
							        }
							        else
							        {
								        matcher.Regex = pc.Regex;
								        matcher.ValueType = ResourceMatcherValueType.Regex;
							        }

							        pc.RpmValues.Insert(0, matcher);
						        }

						        pc.Regex = null;
						        changed = true;
					        }
				        }
				        if (changed)
				        {
					        await mlService.Patch(ml.Id, new MediaLibraryPatchDto
					        {
						        PathConfigurations = pcs.Select(p => new PathConfiguration
						        {
                                    FixedTagIds = p.FixedTagIds,
                                    Path = p.Path,
                                    RpmValues = p.RpmValues
						        }).ToList()
					        });
					        Logger.LogInformation($"Media library {ml} has been migrated successfully.");
				        }
				        else
				        {
					        Logger.LogInformation($"Media library {ml} is clean and no need to be migrated.");
				        }
					}
		        }
	        }
	        catch (Exception e)
	        {
		        Logger.LogError(e, $"An error occurred during migrating media libraries: {e.Message}");
	        }

	        var categoryService = GetRequiredService<ResourceCategoryService>();
            var categories = await categoryService.GetAll();
            var changedCategories = new List<ResourceCategory>();

            var componentOptionsService = GetRequiredService<ComponentOptionsService>();

            var dbCtx = GetRequiredService<InsideWorldDbContext>();
            var playerOptionsList =
                (await dbCtx.CustomPlayerOptionsList.ToListAsync()).ToDictionary(a => a.Id,
                    a => a.ToDto());
            var playableFileSelectorOptionsList =
                (await dbCtx.CustomPlayableFileSelectorOptionsList.ToListAsync()).ToDictionary(a => a.Id,
                    a => a.ToDto());

            var cos = await componentOptionsService.GetAll();
            var newCos = new Dictionary<ComponentType, Dictionary<int, ComponentOptions>>
            {
                {ComponentType.Player, new Dictionary<int, ComponentOptions>()},
                {ComponentType.PlayableFileSelector, new Dictionary<int, ComponentOptions>()},
            };
            foreach (var (k, oo) in playerOptionsList)
            {
                var newVersionOptionsJson = JsonConvert.SerializeObject(oo.ToNewVersion());
                var co = cos.FirstOrDefault(a =>
                    a.ComponentType == ComponentType.Player && a.ComponentAssemblyQualifiedTypeName ==
                    SpecificTypeUtils<CustomPlayer>.Type.AssemblyQualifiedName &&
                    a.Json == newVersionOptionsJson) ?? new ComponentOptions
                {
                    ComponentAssemblyQualifiedTypeName = SpecificTypeUtils<CustomPlayer>.Type.AssemblyQualifiedName,
                    ComponentType = ComponentType.Player,
                    Name = oo.Name,
                    Json = newVersionOptionsJson,
                    Description = null
                };
                newCos[ComponentType.Player][k] = co;
            }

            foreach (var (k, oo) in playableFileSelectorOptionsList)
            {
                var newVersionOptionsJson = JsonConvert.SerializeObject(oo.ToNewVersion());
                var co = cos.FirstOrDefault(a =>
                             a.ComponentType == ComponentType.PlayableFileSelector &&
                             a.ComponentAssemblyQualifiedTypeName ==
                             SpecificTypeUtils<ExtensionBasedPlayableFileSelector>
                                 .Type.AssemblyQualifiedName
                             && a.Json == newVersionOptionsJson) ??
                         new ComponentOptions
                         {
                             ComponentAssemblyQualifiedTypeName =
                                 SpecificTypeUtils<ExtensionBasedPlayableFileSelector>.Type.AssemblyQualifiedName,
                             ComponentType = ComponentType.PlayableFileSelector,
                             Name = oo.Name,
                             Json = newVersionOptionsJson,
                             Description = null
                         };
                newCos[ComponentType.PlayableFileSelector][k] = co;
            }

            var allNewCos = newCos.SelectMany(a => a.Value.Values.Where(b => b.Id == 0)).ToArray();

            Logger.LogInformation(
                $"Moving obsolete options: {string.Join(',', newCos.Select(a => $"{a.Value.Count(b => b.Value.Id == 0)}x{a.Key}"))}");
            if (allNewCos.Any())
            {
                await componentOptionsService.AddRange(allNewCos);
            }

            Logger.LogInformation($"Migrated options: {JsonConvert.SerializeObject(newCos)}");

            const string obsoleteCustomPlayableFileSelectorAssemblyQualifiedName =
                "Bakabase.InsideWorld.Business.Components.Resource.Components.StartFileSelector.CustomPlayableFileSelector, Bakabase.InsideWorld.Business, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null";
            const string obsoleteCustomPlayableFileSelectorsTypeKeyPrefix =
                "Bakabase.InsideWorld.Business.Components.Resource.Components.StartFileSelector";
            const string newCustomPlayableFileSelectorsTypeKeyPrefix =
                "Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector";

            var ccs = new List<CategoryComponent>();
            foreach (var c in categories)
            {
                var cs = new List<CategoryComponent>();
                if (c.ComponentsJsonData.IsNotEmpty())
                {
                    var cds = JsonConvert.DeserializeObject<SimpleComponentData[]>(c.ComponentsJsonData)!;
                    foreach (var cd in cds.Where(a => a.Type != ComponentType.Enhancer))
                    {
                        object? key = null;
                        switch (cd.Type)
                        {
                            case ComponentType.PlayableFileSelector:
                                if (cd is {TypeKey: obsoleteCustomPlayableFileSelectorAssemblyQualifiedName})
                                {
                                    if (newCos.TryGetValue(ComponentType.PlayableFileSelector, out var arr) &&
                                        arr.TryGetValue(cd.CustomOptionsId, out var co))
                                    {
                                        key = co.Id;
                                    }
                                }
                                else
                                {
                                    key = cd.TypeKey.Replace(obsoleteCustomPlayableFileSelectorsTypeKeyPrefix,
                                        newCustomPlayableFileSelectorsTypeKeyPrefix);
                                }

                                break;
                            case ComponentType.Player:
                                if (cd.TypeKey == SpecificTypeUtils<CustomPlayer>.Type.AssemblyQualifiedName)
                                {
                                    if (newCos.TryGetValue(ComponentType.Player, out var arr) &&
                                        arr.TryGetValue(cd.CustomOptionsId, out var co))
                                    {
                                        key = co.Id;
                                    }
                                }
                                else
                                {
                                    key = SpecificTypeUtils<CustomPlayer>.Type.AssemblyQualifiedName!;
                                }

                                break;
                            case ComponentType.Enhancer:
                            default:
                                throw new ArgumentOutOfRangeException();
                        }

                        if (key != null)
                        {
                            var cc = new CategoryComponent
                            {
                                CategoryId = c.Id,
                                ComponentType = cd.Type,
                                ComponentKey = key.ToString()
                            };

                            cs.Add(cc);
                        }
                    }

                    Logger.LogInformation(
                        $"Associated new component options with category [{c.Id}:{c.Name}]. {JsonConvert.SerializeObject(cs)}");
                }

                if (c.EnhancementOptionsJson.IsNotEmpty())
                {
                    var enhancementOptions =
                        JsonConvert.DeserializeObject<Legacies.ResourceCategoryEnhancementOptions>(
                            c.EnhancementOptionsJson)!;
                    var typeKeys = enhancementOptions.EnhancerAssemblyQualifiedTypeNames ?? new string[] { };
                    if (typeKeys?.Any() == true)
                    {
                        foreach (var key in typeKeys)
                        {
                            var cc = new CategoryComponent
                            {
                                CategoryId = c.Id,
                                ComponentType = ComponentType.Enhancer,
                                ComponentKey = key
                            };
                            cs.Add(cc);
                        }
                    }

                    var newEo = new Bakabase.InsideWorld.Models.Models.Dtos.ResourceCategoryEnhancementOptions
                    {
                        DefaultPriority = enhancementOptions.DefaultPriority,
                        EnhancementPriorities = enhancementOptions.EnhancementPriorities
                    };
                    newEo.Standardize(typeKeys);
                    c.EnhancementOptionsJson = JsonConvert.SerializeObject(newEo);
                    changedCategories.Add(c);
                }

                if (cs.Any())
                {
                    ccs.AddRange(cs);
                }
            }

            await using var tran = await categoryService.DbContext.Database.BeginTransactionAsync();

            if (ccs.Any())
            {
                var cs = GetRequiredService<CategoryComponentService>();
                Logger.LogInformation($"Applying {ccs.Count} category components...");
                await cs.AddRange(ccs);
            }

            if (changedCategories.Any())
            {
                await categoryService.UpdateRange(changedCategories);
            }

            await tran.CommitAsync();
            await base.MigrateAfterDbMigrationInternal(context);
        }
    }
}