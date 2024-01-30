using Bakabase.InsideWorld.Business.Components.Downloader.Naming;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Extensions
{
    public class DownloaderUtils
    {
        public static Dictionary<DownloadTaskDtoStatus, DownloadTaskAction[]> AvailableActions = new()
        {
            {
                DownloadTaskDtoStatus.Idle,
                new[]
                {
                    DownloadTaskAction.StartManually, DownloadTaskAction.Disable, 
                    DownloadTaskAction.StartAutomatically
                }
            },
            {DownloadTaskDtoStatus.InQueue, new[] {DownloadTaskAction.Disable}},
            {DownloadTaskDtoStatus.Starting, new DownloadTaskAction[] { }},
            {DownloadTaskDtoStatus.Downloading, new[] {DownloadTaskAction.Disable}},
            {DownloadTaskDtoStatus.Stopping, new DownloadTaskAction[] { }},
            {DownloadTaskDtoStatus.Complete, new[] {DownloadTaskAction.Restart, DownloadTaskAction.Disable}},
            {DownloadTaskDtoStatus.Failed, new[] {DownloadTaskAction.Restart, DownloadTaskAction.Disable}},
            {DownloadTaskDtoStatus.Disabled, new[] {DownloadTaskAction.StartManually}},
        };

        public static Dictionary<int, TimeSpan> IntervalsOnContinuousFailures = new Dictionary<int, TimeSpan>
        {
            {1, TimeSpan.FromMinutes(1)},
            {2, TimeSpan.FromMinutes(5)},
            {3, TimeSpan.FromMinutes(20)},
            {4, TimeSpan.FromMinutes(60)}
        };

        public static string BuildDownloadFilename<TNamingFieldsType>(string namingConvention, Dictionary<string, object> values,
            Dictionary<string, string> wrappers = null)
        {
            var fieldAndReplacers = DownloaderNamingFieldsExtractor<TNamingFieldsType>.FieldsAndReplacers;

            var startIndex = 0;
            var name = namingConvention;
            while (true)
            {
                var (key, index) = fieldAndReplacers.ToDictionary(a => a.Key,
                        a => name.IndexOf(a.Value, startIndex, StringComparison.OrdinalIgnoreCase))
                    .Where(a => a.Value > -1).OrderBy(a => a.Value).FirstOrDefault();
                if (key.IsNotEmpty())
                {
                    var replacement = values.TryGetValue(key, out var value) ? value?.ToString().RemoveInvalidFileNameChars() : null;
                    var replacerLength = fieldAndReplacers[key].Length;
                    var replacementLength = replacement?.Length ?? 0;

                    name = $"{name[..index]}{replacement}{name[(index + replacerLength)..]}";
                    startIndex = index + replacementLength;
                }
                else
                {
                    break;
                }
            }

            if (wrappers?.Any() == true)
            {
                foreach (var wrapper in wrappers)
                {
                    if (wrapper.Key.IsNotEmpty() && wrapper.Value.IsNotEmpty())
                    {
                        name = Regex.Replace(name, $"{Regex.Escape(wrapper.Key)}[\\s]*{Regex.Escape(wrapper.Value)}",
                            string.Empty);
                    }
                }
            }

            return name;
        }
    }
}