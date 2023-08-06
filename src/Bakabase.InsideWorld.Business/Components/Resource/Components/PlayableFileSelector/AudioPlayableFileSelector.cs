using System.Linq;
using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
using Bakabase.InsideWorld.Models.Configs.CustomOptions;
using Bakabase.InsideWorld.Models.Constants;
using NPOI.SS.Formula.Functions;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector
{
    [PlayableFileSelector(Name = nameof(AudioPlayableFileSelector),
        Description = "Select all common audio files as playable files.")]
    public class AudioPlayableFileSelector : ExtensionBasedPlayableFileSelector
    {
        public AudioPlayableFileSelector() : base(new ExtensionBasedPlayableFileSelectorOptions
        {
            Extensions = BusinessConstants.AudioExtensions.ToArray(),
            MaxFileCount = int.MaxValue
        })
        {
        }
    }
}