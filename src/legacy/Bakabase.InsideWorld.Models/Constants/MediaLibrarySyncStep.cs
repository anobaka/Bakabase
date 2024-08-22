using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Models.Constants
{
    public enum MediaLibrarySyncStep
    {
        [MediaLibrarySyncStep(10)] Filtering,
        [MediaLibrarySyncStep(10)] AcquireFileSystemInfo,
        [MediaLibrarySyncStep(5)] CleanResources,

        // [MediaLibrarySyncStep(15)] DiscoverPlayableFiles,
        // [MediaLibrarySyncStep(15)] DiscoverCovers,
        // [MediaLibrarySyncStep(20)] ResampleCovers,
        // [MediaLibrarySyncStep(25)] AnalyzeResources,
        [MediaLibrarySyncStep(15)] CompareResources,
        [MediaLibrarySyncStep(10)] SaveResources,
    }

    public static class MediaLibrarySyncStepExtensions
    {
        public static readonly Dictionary<MediaLibrarySyncStep, int> Percentages;

        static MediaLibrarySyncStepExtensions()
        {
            var weights = SpecificEnumUtils<MediaLibrarySyncStep>.Values.ToDictionary(a => a,
                a => a.GetAttribute<MediaLibrarySyncStepAttribute>()?.Weight ?? 0);
            var totalWeight = weights.Values.Sum();
            if (totalWeight > 0)
            {
                Percentages = weights.ToDictionary(a => a.Key, a => a.Value * 100 / totalWeight);
            }
        }

        public static int GetBasePercentage(this MediaLibrarySyncStep step)
        {
            return Percentages.Where(a => a.Key < step).Sum(a => a.Value);
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class MediaLibrarySyncStepAttribute : Attribute
    {
        public MediaLibrarySyncStepAttribute(int weight)
        {
            Weight = weight;
        }

        public int Weight { get; set; }
    }
}