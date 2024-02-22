using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.BmVolumeProcessor;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures.BmMultipleValueProcessor;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures.BmSimpleValueProcessor;
using Bakabase.InsideWorld.Business.Components.TextProcessing;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures
{
    public static class BmProcessorExtensions
    {
        public static int ToCategoryProcessorValue(this BulkModificationProcess process)
        {
            return JsonSerializableValue<int>.Parse(process.Value)!;
        }

        public static int ToMediaLibraryProcessorValue(this BulkModificationProcess process)
        {
            return JsonSerializableValue<int>.Parse(process.Value)!;
        }

        public static TextProcessValue ToNameProcessorValue(this BulkModificationProcess process)
        {
            return TextProcessValue.Parse(process.Value)!;
        }

        // FileName

        public static TextProcessValue ToFileNameProcessorValue(this BulkModificationProcess process)
        {
            return TextProcessValue.Parse(process.Value)!;
        }

        // Directory Path

        public static TextProcessValue ToDirectoryPathProcessorValue(this BulkModificationProcess process)
        {
            return TextProcessValue.Parse(process.Value)!;
        }

        // ReleaseDt

        public static BmSimpleValueProcessorValue<string> ToReleaseDtProcessorValue(
            this BulkModificationProcess process)
        {
            return BmSimpleValueProcessorValue<string>.Parse(process.Value)!;
        }

        // CreateDt

        public static BmSimpleValueProcessorValue<DateTime> ToCreateDtProcessorValue(this BulkModificationProcess process)
        {
            return BmSimpleValueProcessorValue<DateTime>.Parse(process.Value)!;
        }

        // FileCreateDt
        public static BmSimpleValueProcessorValue<DateTime> ToFileCreateDtProcessorValue(
            this BulkModificationProcess process)
        {
            return BmSimpleValueProcessorValue<DateTime>.Parse(process.Value)!;
        }

        // FileModifyDt
        public static BmSimpleValueProcessorValue<DateTime> ToFileModifyDtProcessorValue(
            this BulkModificationProcess process)
        {
            return BmSimpleValueProcessorValue<DateTime>.Parse(process.Value)!;
        }

        // Publisher
        public static BmMultipleValueProcessorValue<int, string, TextProcessValue> ToPublisherProcessorValue(
            this BulkModificationProcess process)
        {
            return BmMultipleValueProcessorValue<int, string, TextProcessValue>.Parse(process.Value)!;
        }

        // Language

        public static BmSimpleValueProcessorValue<string> ToLanguageProcessorValue(
            this BulkModificationProcess process)
        {
            return BmSimpleValueProcessorValue<string>.Parse(process.Value)!;
        }

        // Volume

        public static BmVolumeProcessorValue ToVolumeProcessorValue(this BulkModificationProcess process)
        {
            return BmVolumeProcessorValue.Parse(process.Value)!;
        }

        // Original

        public static BmMultipleValueProcessorValue<int, string, TextProcessValue> ToOriginalProcessorValue(
            this BulkModificationProcess process)
        {
            return BmMultipleValueProcessorValue<int, string, TextProcessValue>.Parse(process.Value)!;
        }

        // Series

        public static TextProcessValue ToSeriesProcessorValue(this BulkModificationProcess process)
        {
            return TextProcessValue.Parse(process.Value)!;
        }

        // Tag

        public static BmMultipleValueProcessorValue<int, string, TextProcessValue> ToTagProcessorValue(
            this BulkModificationProcess process)
        {
            return BmMultipleValueProcessorValue<int, string, TextProcessValue>.Parse(process.Value)!;
        }

        // Introduction

        public static TextProcessValue ToIntroductionProcessorValue(this BulkModificationProcess process)
        {
            return TextProcessValue.Parse(process.Value)!;
        }

        // Rate
        public static BmSimpleValueProcessorValue<string> ToRateProcessorValue(
            this BulkModificationProcess process)
        {
            return BmSimpleValueProcessorValue<string>.Parse(process.Value)!;
        }

        // CustomProperty
        public static TextProcessValue ToCustomPropertyProcessorValue(this BulkModificationProcess process)
        {
            return TextProcessValue.Parse(process.Value)!;
        }
    }
}