using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Models.Extensions
{
    public static class EnhancementRecordExtensions
    {
        public static EnhancementRecordDto ToDto(this EnhancementRecord record)
        {
            if (record == null)
            {
                return null;
            }

            return new EnhancementRecordDto
            {
                Id = record.Id,
                CreateDt = record.CreateDt,
                Enhancement = record.Enhancement,
                EnhancerDescriptorId = record.EnhancerDescriptorId,
                EnhancerName = record.EnhancerName,
                Message = record.Message,
                ResourceId = record.ResourceId,
                RuleId = record.RuleId,
                Success = record.Success,
                ResourceRawFullName = record.ResourceRawFullName
            };
        }
    }
}