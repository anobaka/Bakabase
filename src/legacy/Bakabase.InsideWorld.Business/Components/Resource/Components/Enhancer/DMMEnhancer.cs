// using System;
// using System.Collections.Generic;
// using System.Linq;
// using System.Text;
// using System.Threading.Tasks;
// using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
// using Bakabase.InsideWorld.Models.Constants;
// using Bakabase.InsideWorld.Models.Models.Dtos;
//
// namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer
// {
//     [Enhancer(new object[]
//     {
//         ReservedResourceProperty.Name,
//         ReservedResourceProperty.Introduction,
//         ReservedResourceProperty.Series,
//         ReservedResourceProperty.Publisher,
//         ReservedResourceProperty.ReleaseDt,
//     }, new object[]
//     {
//         ReservedResourceFileType.Cover
//     })]
//     public class DMMEnhancer : IEnhancer
//     {
//         public async Task<Enhancement[]> Enhance(ResourceDto resource)
//         {
//             // Cover
//             // Release Dt
//             // Name
//             // Publisher
//             // Introduce
//             // Tags
//             return null;
//         }
//
//         public Task<string> Validate()
//         {
//             return null;
//         }
//     }
// }