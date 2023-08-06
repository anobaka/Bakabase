using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class FullnameAnalyzeRequestModel
    {
        [BindRequired] public int CategoryId { get; set; }
        [Required] public string Fullname1 { get; set; }
        public string Fullname2 { get; set; }
    }
}