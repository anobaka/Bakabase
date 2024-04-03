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
    public enum BulkModificationFilterableProperty
    {
        [BulkModificationPropertyFilter([FO.In, FO.NotIn, FO.Equals, FO.NotEquals])]
        Category = 1,

        [BulkModificationPropertyFilter([FO.In, FO.NotIn, FO.Equals, FO.NotEquals])]
        MediaLibrary = 2,

        // [BulkModificationPropertyFilter(
        // [
        //     FO.Equals, FO.NotEquals, FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
        //     FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull, FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        // ])]
        // Name = 3,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
            FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull, FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        ])]
        FileName = 4,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
            FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull, FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        ])]
        DirectoryPath = 5,
        //
        // [BulkModificationPropertyFilter(
        // [
        //     FO.Equals, FO.NotEquals, FO.GreaterThan, FO.LessThan, FO.GreaterThanOrEquals, FO.LessThanOrEquals,
        //     FO.IsNotNull, FO.IsNull
        // ])]
        // ReleaseDt = 6,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.GreaterThan, FO.LessThan, FO.GreaterThanOrEquals, FO.LessThanOrEquals,
            FO.IsNotNull, FO.IsNull
        ])]
        CreateDt = 7,

        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.GreaterThan, FO.LessThan, FO.GreaterThanOrEquals, FO.LessThanOrEquals,
            FO.IsNotNull, FO.IsNull
        ])]
        FileCreateDt = 8,
        
        [BulkModificationPropertyFilter(
        [
            FO.Equals, FO.NotEquals, FO.GreaterThan, FO.LessThan, FO.GreaterThanOrEquals, FO.LessThanOrEquals,
            FO.IsNotNull, FO.IsNull
        ])]
        FileModifyDt = 9,

        // [BulkModificationPropertyFilter(
        // [
        //     FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
        //     FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull,
        //     FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        // ])]
        // Publisher = 10,
        //
        // [BulkModificationPropertyFilter(
        //     [FO.In, FO.NotIn, FO.Equals, FO.NotEquals, FO.IsNotNull, FO.IsNull])]
        // Language = 11,
        //
        // [BulkModificationPropertyFilter(
        // [
        //     FO.Equals, FO.NotEquals, FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
        //     FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull, FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        // ])]
        // Volume = 12,
        //
        // [BulkModificationPropertyFilter(
        // [
        //     FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
        //     FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull,
        //     FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        // ])]
        // Original = 13,
        //
        // [BulkModificationPropertyFilter(
        // [
        //     FO.Equals, FO.NotEquals, FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
        //     FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull, FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        // ])]
        // Series = 14,

        [BulkModificationPropertyFilter(
        [
            FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
            FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull,
            FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        ])]
        Tag = 15,

        // [BulkModificationPropertyFilter(
        // [
        //     FO.Equals, FO.NotEquals, FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
        //     FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull, FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        // ])]
        // Introduction = 16,
        //
        // [BulkModificationPropertyFilter(
        // [
        //     FO.Equals, FO.NotEquals, FO.GreaterThan, FO.LessThan, FO.GreaterThanOrEquals,
        //     FO.LessThanOrEquals, FO.IsNotNull, FO.IsNull
        // ])]
        // Rate = 17,
        //
        // [BulkModificationPropertyFilter(
        // [
        //     FO.Contains, FO.NotContains, FO.StartsWith, FO.NotStartsWith,
        //     FO.EndsWith, FO.NotEndsWith, FO.IsNotNull, FO.IsNull,
        //     FO.In, FO.NotIn, FO.Matches, FO.NotMatches
        // ])]
        // CustomProperty = 18
    }
}