using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Business.Models.Input
{
    public class CategorySetupWizardInputModel
    {
        public CategoryAddInputModel Category { get; set; } = null!;
        public MediaLibrary[]? MediaLibraries { get; set; }
        public bool SyncAfterSaving { get; set; }
    }
}