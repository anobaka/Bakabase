using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class CategorySetupWizardRequestModel
    {
        public ResourceCategoryAddRequestModel Category { get; set; } = null!;
        public MediaLibraryDto[]? MediaLibraries { get; set; }
        public bool SyncAfterSaving { get; set; }
    }
}