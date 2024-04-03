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
        protected override BulkModificationFilterableProperty Property => BulkModificationFilterableProperty.Category;

        protected override void SetProperty(Models.Domain.Resource resource, int value, BulkModificationDiff diff)
        {
            resource.CategoryId = value;
        }
    }

    public class BmMediaLibraryDiffHandler : BmAbstractDiffHandler<int>
    {
        protected override BulkModificationFilterableProperty Property => BulkModificationFilterableProperty.MediaLibrary;

        protected override void SetProperty(Models.Domain.Resource resource, int value, BulkModificationDiff diff)
        {
            resource.MediaLibraryId = value;
        }
    }

    // // FileName
    // public class BmFileNameDiffHandler : BmAbstractDiffHandler<string>
    // {
    //     protected override BulkModificationProperty Property => BulkModificationProperty.FileName;
    //
    //     protected override void SetProperty(Models.Domain.Resource resource, string value, BulkModificationDiff diff)
    //     {
    //         throw new NotImplementedException();
    //     }
    // }

    // // DirectoryPath
    // public class BmDirectoryPathDiffHandler : BmAbstractDiffHandler<string>
    // {
    //     protected override BulkModificationProperty Property => BulkModificationProperty.DirectoryPath;
    //
    //     protected override void SetProperty(Models.Domain.Resource resource, string value, BulkModificationDiff diff)
    //     {
    //         resource.DirectoryPath = value;
    //     }
    // }

    // CreateDt
    public class BmCreateDtDiffHandler : BmAbstractDiffHandler<DateTime>
    {
        protected override BulkModificationFilterableProperty Property => BulkModificationFilterableProperty.CreateDt;

        protected override void SetProperty(Models.Domain.Resource resource, DateTime value, BulkModificationDiff diff)
        {
            resource.CreateDt = value;
        }
    }

    // FileCreateDt
    public class BmFileCreateDtDiffHandler : BmAbstractDiffHandler<DateTime>
    {
        protected override BulkModificationFilterableProperty Property => BulkModificationFilterableProperty.FileCreateDt;

        protected override void SetProperty(Models.Domain.Resource resource, DateTime value, BulkModificationDiff diff)
        {
            resource.FileCreateDt = value;
        }
    }

    // FileModifyDt
    public class BmFileModifyDtDiffHandler : BmAbstractDiffHandler<DateTime>
    {
        protected override BulkModificationFilterableProperty Property => BulkModificationFilterableProperty.FileModifyDt;

        protected override void SetProperty(Models.Domain.Resource resource, DateTime value, BulkModificationDiff diff)
        {
            resource.FileModifyDt = value;
        }
    }

    // Tag
    public class BmTagDiffHandler : BmAbstractDiffHandler<List<TagDto>>
    {
        protected override BulkModificationFilterableProperty Property => BulkModificationFilterableProperty.Tag;

        protected override void SetProperty(Models.Domain.Resource resource, List<TagDto>? value, BulkModificationDiff diff)
        {
            resource.Tags = value;
        }
    }
}