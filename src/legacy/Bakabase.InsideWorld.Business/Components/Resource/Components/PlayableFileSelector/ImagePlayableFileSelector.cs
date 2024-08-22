using System.Linq;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Models.Configs.CustomOptions;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector
{
    [PlayableFileSelector(Name = nameof(ImagePlayableFileSelector),
        Description = "Select the first image file as a playable file.")]
    public class ImagePlayableFileSelector : ExtensionBasedPlayableFileSelector
    {
        public ImagePlayableFileSelector() : base(new ExtensionBasedPlayableFileSelectorOptions
        {
            Extensions = InternalOptions.ImageExtensions.ToArray(),
            MaxFileCount = 1
        })
        {
        }
    }
}