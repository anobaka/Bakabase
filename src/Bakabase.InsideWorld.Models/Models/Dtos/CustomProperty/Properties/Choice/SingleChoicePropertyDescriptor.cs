using System;
using System.Linq;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Choice
{
    public class SingleChoicePropertyDescriptor : AbstractCustomPropertyDescriptor<SingleChoiceProperty,
        ChoicePropertyOptions<string>, SingleChoicePropertyValue, string>
    {
        public override CustomPropertyType Type => CustomPropertyType.SingleChoice;

        protected override bool IsMatch(string? id, CustomPropertyValueSearchRequestModel model)
        {
            switch (model.Operation)
            {
                case SearchOperation.Equals:
                case SearchOperation.NotEquals:
                    {

                        var searchId = string.IsNullOrEmpty(model.Value)
                            ? null
                            : JsonConvert.DeserializeObject<string>(model.Value);
                        // invalid filter
                        if (string.IsNullOrEmpty(searchId))
                        {
                            return true;
                        }

                        return model.Operation == SearchOperation.Equals
                            ? string.Equals(searchId, id)
                            : !string.Equals(searchId, id);
                    }
                // case CustomPropertyValueSearchOperation.Contains:
                // 	break;
                // case CustomPropertyValueSearchOperation.NotContains:
                // 	break;
                // case CustomPropertyValueSearchOperation.StartsWith:
                //  break;
                // case CustomPropertyValueSearchOperation.NotStartsWith:
                //  break;
                // case CustomPropertyValueSearchOperation.EndsWith:
                //  break;
                // case CustomPropertyValueSearchOperation.NotEndsWith:
                //  break;
                // case CustomPropertyValueSearchOperation.GreaterThan:
                //  break;
                // case CustomPropertyValueSearchOperation.LessThan:
                //  break;
                // case CustomPropertyValueSearchOperation.GreaterThanOrEquals:
                //  break;
                // case CustomPropertyValueSearchOperation.LessThanOrEquals:
                //  break;
                case SearchOperation.IsNull:
                    return string.IsNullOrEmpty(id);
                case SearchOperation.IsNotNull:
                    return !string.IsNullOrEmpty(id);
                case SearchOperation.In:
                case SearchOperation.NotIn:
                    {
                        var searchIds = string.IsNullOrEmpty(model.Value)
                            ? null
                            : JsonConvert.DeserializeObject<string[]>(model.Value);
                        if (searchIds?.Any() == true)
                        {
                            return model.Operation == SearchOperation.In
                                ? searchIds.Contains(id)
                                : !searchIds.Contains(id);
                        }

                        // invalid filter
                        return true;
                    }
                // case CustomPropertyValueSearchOperation.Matches:
                //  break;
                // case CustomPropertyValueSearchOperation.NotMatches:
                //  break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}