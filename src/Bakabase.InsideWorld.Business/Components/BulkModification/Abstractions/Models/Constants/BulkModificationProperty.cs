using Bakabase.InsideWorld.Models.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using FO = Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants.BulkModificationFilterOperation;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants
{
    public enum BulkModificationProperty
    {
        [BulkModificationPropertyFilter([FO.In, FO.NotIn, FO.Equals, FO.NotEquals])]
        Category = 1,

        [BulkModificationPropertyFilter([FO.In, FO.NotIn, FO.Equals, FO.NotEquals])]
        MediaLibrary,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
            FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull, FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        ])]
        Name,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
            FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull, FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        ])]
        FileName,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
            FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull, FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        ])]
        DirectoryPath,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.GreaterThan, FO.LessThan, FO.GreaterThanOrEquals, FO.LessThanOrEquals,
            FO.IsNotNull, FO.IsNull
        ])]
        ReleaseDt,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.GreaterThan, FO.LessThan, FO.GreaterThanOrEquals, FO.LessThanOrEquals,
            FO.IsNotNull, FO.IsNull
        ])]
        CreateDt,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.GreaterThan, FO.LessThan, FO.GreaterThanOrEquals, FO.LessThanOrEquals,
            FO.IsNotNull, FO.IsNull
        ])]
        FileCreateDt,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.GreaterThan, FO.LessThan, FO.GreaterThanOrEquals, FO.LessThanOrEquals,
            FO.IsNotNull, FO.IsNull
        ])]
        FileModifyDt,

        [BulkModificationPropertyFilter(
        [
            FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
            FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull,
            FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        ])]
        Publisher,

        [BulkModificationPropertyFilter(
            [FO.In, FO.NotIn, FO.Equals, FO.NotEquals, FO.IsNotNull, FO.IsNull])]
        Language,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
            FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull, FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        ])]
        Volume,

        [BulkModificationPropertyFilter(
        [
            FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
            FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull,
            FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        ])]
        Original,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
            FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull, FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        ])]
        Series,

        [BulkModificationPropertyFilter(
        [
            FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
            FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull,
            FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        ])]
        Tag,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
            FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull, FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        ])]
        Introduction,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.GreaterThan, FO.LessThan, FO.GreaterThanOrEquals,
            FO.LessThanOrEquals, FO.IsNotNull, FO.IsNull
        ])]
        Rate,

        [BulkModificationPropertyFilter(
        [
            FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
            FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull,
            FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        ])]
        CustomProperty,
    }
}