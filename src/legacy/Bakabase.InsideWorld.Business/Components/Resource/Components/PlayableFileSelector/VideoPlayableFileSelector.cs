using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Models.Configs.CustomOptions;
using Bakabase.InsideWorld.Models.Constants;
using System.Linq;
using Bakabase.Abstractions.Components.Configuration;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector
{
    [PlayableFileSelector(Name = nameof(VideoPlayableFileSelector),
        Description = "Select all common video files as playable files.")]
    public class VideoPlayableFileSelector : ExtensionBasedPlayableFileSelector
    {
        public VideoPlayableFileSelector() : base(new ExtensionBasedPlayableFileSelectorOptions
        {
            Extensions = InternalOptions.VideoExtensions.ToArray(),
            MaxFileCount = int.MaxValue
        })
        {
        }
    }
}