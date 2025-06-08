using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Business.Configurations.Models.Domain;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Configurations
{
    /// <summary>
    /// This type is used on enumerable operations only. May be marked as Obsolete.
    /// </summary>
    public class InsideWorldOptionsManagerPool
    {
        public readonly IBOptionsManager<AppOptions> App;
        public readonly IBOptionsManager<UIOptions> UI;
        public readonly IBOptionsManager<BilibiliOptions> Bilibili;
        public readonly IBOptionsManager<ExHentaiOptions> ExHentai;
        public readonly IBOptionsManager<FileSystemOptions> FileSystem;
        public readonly IBOptionsManager<JavLibraryOptions> JavLibrary;
        public readonly IBOptionsManager<PixivOptions> Pixiv;
        public readonly IBOptionsManager<ThirdPartyOptions> ThirdParty;
        public readonly IBOptionsManager<ResourceOptions> Resource;
        public readonly IBOptionsManager<NetworkOptions> Network;
        public readonly IBOptionsManager<MigrationOptions> Migration;
        public readonly IBOptionsManager<EnhancerOptions> Enhancer;
        public readonly IBOptionsManager<TaskOptions> Task;
        public readonly IBOptionsManager<AiOptions> Ai;
        public readonly IBOptionsManager<SoulPlusOptions> SoulPlus;

        public InsideWorldOptionsManagerPool(IBOptionsManager<UIOptions> ui, IBOptionsManager<BilibiliOptions> bilibili,
            IBOptionsManager<ExHentaiOptions> exHentai, IBOptionsManager<FileSystemOptions> fileSystem,
            IBOptionsManager<JavLibraryOptions> javLibrary, IBOptionsManager<PixivOptions> pixiv,
            IBOptionsManager<ThirdPartyOptions> thirdParty, IBOptionsManager<ResourceOptions> resource,
            IBOptionsManager<AppOptions> app, IBOptionsManager<NetworkOptions> network,
            IBOptionsManager<MigrationOptions> migration, IBOptionsManager<EnhancerOptions> enhancer, IBOptionsManager<TaskOptions> task, IBOptionsManager<AiOptions> ai, IBOptionsManager<SoulPlusOptions> soulPlus)
        {
            UI = ui;
            Bilibili = bilibili;
            ExHentai = exHentai;
            FileSystem = fileSystem;
            JavLibrary = javLibrary;
            Pixiv = pixiv;
            ThirdParty = thirdParty;
            Resource = resource;
            App = app;
            Network = network;
            Migration = migration;
            Enhancer = enhancer;
            Task = task;
            Ai = ai;
            SoulPlus = soulPlus;

            AllOptionsManagers = SpecificTypeUtils<InsideWorldOptionsManagerPool>.Type.GetFields()
                .Where(a =>
                {
                    var type = a.FieldType;
                    if (type.IsGenericType)
                    {
                        return type.GetGenericTypeDefinition() == typeof(IBOptionsManager<>);
                    }

                    return false;
                })
                .ToDictionary(a => a.FieldType.GetGenericArguments()[0], a => a.GetValue(this)!);
        }

        public Dictionary<Type, object> AllOptionsManagers { get; }
    }
}