// using System.Collections.Generic;
// using System.Linq;
// using Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector.Infrastructures;
// using Bakabase.InsideWorld.Models.Configs.CustomOptions;
//
// namespace Bakabase.InsideWorld.Business.Components.Resource.Components.PlayableFileSelector
// {
//     [PlayableFileSelector(OptionsType = typeof(ExtensionBasedPlayableFileSelectorOptions))]
//     public class CustomPlayableFileSelector : ExtensionBasedPlayableFileSelector
//     {
//         public CustomPlayableFileSelector(IEnumerable<string> extensions, int maxFileCount, string name)
//         {
//             Extensions = extensions.ToHashSet();
//             MaxFileCount = maxFileCount;
//             Name = name;
//         }
//     }
// }