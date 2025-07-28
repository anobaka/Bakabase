using System;
using System.Collections.Generic;
using System.Linq;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components
{
    public class RangeCheckpointContext
    {
        private readonly Dictionary<string, string?> _ranges;

        private string? _firstId;
        private string? _currentStartId;
        private string? _currentEndId;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="checkpoint">
        /// 1028-372,39992-102,1482,2782,...
        /// </param>
        public RangeCheckpointContext(string? checkpoint)
        {
            _ranges = GetRanges(checkpoint);
        }

        public AnalyzeResult Analyze(string id)
        {
            if (_firstId.IsNullOrEmpty())
            {
                _firstId = id;
            }

            if (_currentStartId.IsNullOrEmpty() && _ranges.TryGetValue(id, out var endId))
            {
                // Task done
                if (endId.IsNullOrEmpty())
                {
                    return AnalyzeResult.AllTaskIsDone;
                }
                else
                {
                    _currentStartId = id;
                    // Skip to end Id
                    _currentEndId = endId;
                }
            }

            if (_currentEndId.IsNotEmpty())
            {
                if (id != _currentEndId)
                {
                    // do nothing to skip
                }
                else
                {
                    _ranges.Remove(_currentStartId!, out _);
                    _currentStartId = null;
                    _currentEndId = null;
                    // do nothing to skip
                }

                return AnalyzeResult.Skip;
            }
            else
            {
                return AnalyzeResult.Download;
            }
        }

        public string? BuildCheckpointOnComplete()
        {
            return BuildCheckpoint(new Dictionary<string, string?> {{_firstId, null}});
        }

        public string BuildCheckpoint(string id)
        {
            var newRanges = new Dictionary<string, string?>(_ranges)
            {
                [_firstId] = _currentEndId.IsNotEmpty()
                    ? _currentEndId
                    : id
            };
            return BuildCheckpoint(newRanges);
        }

        public enum AnalyzeResult
        {
            AllTaskIsDone = 1,
            Skip = 2,
            Download = 3
        }

        private static Dictionary<string, string?> GetRanges(string? checkpoint)
        {
            var ranges = new Dictionary<string, string?>();

            if (checkpoint.IsNotEmpty())
            {
                var rangeTexts = checkpoint.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(a => a.Trim())
                    .ToArray();

                foreach (var rangeText in rangeTexts)
                {
                    var segments = rangeText.Split('-', StringSplitOptions.RemoveEmptyEntries).Select(a => a.Trim())
                        .ToArray();
                    if (segments.Any())
                    {
                        var start = segments[0];
                        string end = null;
                        if (rangeText.Contains('-'))
                        {
                            if (!rangeText.EndsWith('-'))
                            {
                                if (segments.Length > 1)
                                {
                                    end = segments[1];
                                }
                            }
                        }
                        else
                        {
                            end = start;
                        }

                        ranges.TryAdd(start, end);
                    }
                }
            }

            return ranges;
        }

        private static string BuildCheckpoint(Dictionary<string, string?> ranges) => string.Join(',',
            ranges.OrderByDescending(a => a.Key, StringComparer.OrdinalIgnoreCase)
                .Select(a => a.Key == a.Value ? a.Key : $"{a.Key}-{a.Value}"));
    }
}