using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;

namespace Bakabase.Modules.Enhancer.Abstractions.Components;

public interface IEnhancerLocalizer
{
    string Enhancer_Name(EnhancerId enhancerId);
    string? Enhancer_Description(EnhancerId enhancerId);
    string Enhancer_TargetName(EnhancerId enhancerId, Enum target);
    string? Enhancer_TargetDescription(EnhancerId enhancerId, Enum target);
    string Enhancer_Target_Options_PropertyTypeIsNotSupported(PropertyPool type);
    string Enhancer_Target_Options_PropertyIdIsNullButPropertyTypeIsNot(PropertyPool type, string target);
    string Enhancer_Target_Options_PropertyTypeIsNullButPropertyIdIsNot(int id, string target);
    string Enhancer_Target_Options_PropertyIdIsNotFoundInReservedResourceProperties(int id);
    string Enhancer_Target_Options_PropertyIdIsNotFoundInCustomResourceProperties(int id);
    string Enhancer_DeletingEnhancementRecords(int count);
    string Enhancer_ReApplyingEnhancements(int count);
    string Enhance();
    string Enhancer_KeywordPropertyIsEmpty(string poolName, string propertyName, string scopeName);
    string Enhancer_KeywordIsEmpty();
    string Enhancer_UsePropertyAsKeyword(string poolName, string propertyName, string scopeName);
    string Enhancer_UseFilenameAsKeyword(string filename);
    string Enhancer_KeywordPretreatStatus(bool enabled);
    string Enhancer_KeywordAfterPretreatment(string keyword);
}