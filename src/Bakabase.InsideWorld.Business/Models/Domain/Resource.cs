using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using CsQuery.ExtensionMethods.Internal;

namespace Bakabase.InsideWorld.Business.Models.Domain
{
    public record Resource : Abstractions.Models.Domain.Resource
    {

    }
}