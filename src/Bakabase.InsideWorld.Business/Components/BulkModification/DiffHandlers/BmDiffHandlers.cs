using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using NPOI.HPSF;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.DiffHandlers
{
    public class BmCategoryDiffHandler : BmAbstractDiffHandler<int>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.Category;

        protected override void SetProperty(ResourceDto resource, int value, BulkModificationDiff diff)
        {
            resource.CategoryId = value;
        }
    }

    public class BmMediaLibraryDiffHandler : BmAbstractDiffHandler<int>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.MediaLibrary;

        protected override void SetProperty(ResourceDto resource, int value, BulkModificationDiff diff)
        {
            resource.MediaLibraryId = value;
        }
    }

    // Name
    public class BmNameDiffHandler : BmAbstractDiffHandler<string>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.Name;

        protected override void SetProperty(ResourceDto resource, string value, BulkModificationDiff diff)
        {
            resource.Name = value;
        }
    }

    // // FileName
    // public class BmFileNameDiffHandler : BmAbstractDiffHandler<string>
    // {
    //     protected override BulkModificationProperty Property => BulkModificationProperty.FileName;
    //
    //     protected override void SetProperty(ResourceDto resource, string value, BulkModificationDiff diff)
    //     {
    //         throw new NotImplementedException();
    //     }
    // }

    // // DirectoryPath
    // public class BmDirectoryPathDiffHandler : BmAbstractDiffHandler<string>
    // {
    //     protected override BulkModificationProperty Property => BulkModificationProperty.DirectoryPath;
    //
    //     protected override void SetProperty(ResourceDto resource, string value, BulkModificationDiff diff)
    //     {
    //         resource.DirectoryPath = value;
    //     }
    // }

    // ReleaseDt
    public class BmReleaseDtDiffHandler : BmAbstractDiffHandler<DateTime?>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.ReleaseDt;

        protected override void SetProperty(ResourceDto resource, DateTime? value, BulkModificationDiff diff)
        {
            resource.ReleaseDt = value;
        }
    }

    // CreateDt
    public class BmCreateDtDiffHandler : BmAbstractDiffHandler<DateTime>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.CreateDt;

        protected override void SetProperty(ResourceDto resource, DateTime value, BulkModificationDiff diff)
        {
            resource.CreateDt = value;
        }
    }

    // FileCreateDt
    public class BmFileCreateDtDiffHandler : BmAbstractDiffHandler<DateTime>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.FileCreateDt;

        protected override void SetProperty(ResourceDto resource, DateTime value, BulkModificationDiff diff)
        {
            resource.FileCreateDt = value;
        }
    }

    // FileModifyDt
    public class BmFileModifyDtDiffHandler : BmAbstractDiffHandler<DateTime>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.FileModifyDt;

        protected override void SetProperty(ResourceDto resource, DateTime value, BulkModificationDiff diff)
        {
            resource.FileModifyDt = value;
        }
    }

    // Publisher

    public class BmPublisherDiffHandler : BmAbstractDiffHandler<List<PublisherDto>>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.Publisher;

        protected override void SetProperty(ResourceDto resource, List<PublisherDto>? value, BulkModificationDiff diff)
        {
            resource.Publishers = value;
        }
    }

    // Language

    public class BmLanguageDiffHandler : BmAbstractDiffHandler<ResourceLanguage>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.Language;

        protected override void SetProperty(ResourceDto resource, ResourceLanguage value, BulkModificationDiff diff)
        {
            resource.Language = value;
        }
    }

    // Volume
    public class BmVolumeDiffHandler : BmAbstractDiffHandler<VolumeDto>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.Volume;

        protected override void SetProperty(ResourceDto resource, VolumeDto? value, BulkModificationDiff diff)
        {
            resource.Volume = value;
        }
    }

    // Original
    public class BmOriginalDiffHandler : BmAbstractDiffHandler<List<OriginalDto>>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.Original;

        protected override void SetProperty(ResourceDto resource, List<OriginalDto>? value, BulkModificationDiff diff)
        {
            resource.Originals = value;
        }
    }

    // Series
    public class BmSeriesDiffHandler : BmAbstractDiffHandler<SeriesDto>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.Series;

        protected override void SetProperty(ResourceDto resource, SeriesDto? value, BulkModificationDiff diff)
        {
            resource.Series = value;
        }
    }

    // Tag
    public class BmTagDiffHandler : BmAbstractDiffHandler<List<TagDto>>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.Tag;

        protected override void SetProperty(ResourceDto resource, List<TagDto>? value, BulkModificationDiff diff)
        {
            resource.Tags = value;
        }
    }

    // Introduction
    public class BmIntroductionDiffHandler : BmAbstractDiffHandler<string>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.Introduction;

        protected override void SetProperty(ResourceDto resource, string value, BulkModificationDiff diff)
        {
            resource.Introduction = value;
        }
    }

    // Rate
    public class BmRateDiffHandler : BmAbstractDiffHandler<decimal>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.Rate;

        protected override void SetProperty(ResourceDto resource, decimal value, BulkModificationDiff diff)
        {
            resource.Rate = value;
        }
    }

    // CustomProperty
    public class BmCustomPropertyDiffHandler : BmAbstractDiffHandler<List<CustomResourceProperty>>
    {
        protected override BulkModificationProperty Property => BulkModificationProperty.CustomProperty;

        protected override void SetProperty(ResourceDto resource, List<CustomResourceProperty>? value,
            BulkModificationDiff diff)
        {
            resource.CustomProperties = value?.GroupBy(x => x.Key).ToDictionary(x => x.Key, x => x.ToList());
        }
    }
}