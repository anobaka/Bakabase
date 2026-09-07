using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models
{
    public class DownloadTask
    {
        public int Id { get; set; }
        public string Key { get; set; } = null!;

        /// <summary>
        /// Populated during downloading
        /// </summary>
        public string? Name { get; set; }

        public ThirdPartyId ThirdPartyId { get; set; }
        public int Type { get; set; }
        public decimal Progress { get; set; }
        public DateTime DownloadStatusUpdateDt { get; set; }
        public long? Interval { get; set; }
        public int? StartPage { get; set; }
        public int? EndPage { get; set; }
        public string? Message { get; set; }
        public string? Checkpoint { get; set; }
        public DownloadTaskStatus Status { get; set; }
        public string DownloadPath { get; set; } = null!;
        public string? Current { get; set; }
        public int FailureTimes { get; set; }
        public bool AutoRetry { get; set; }
        public DateTime? NextStartDt { get; set; }
        public HashSet<DownloadTaskAction> AvailableActions { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string? Options { get; set; }

        /// <summary>
        /// Display-oriented view of what running this task has taught us. Null when there is nothing
        /// to say. Read-only for the client — <see cref="Options"/> remains the thing it edits.
        /// </summary>
        public DownloadTaskMetadata? Metadata { get; set; }

        /// <summary>
        /// The task's source-specific options, or the source's defaults when there are none.
        /// </summary>
        /// <remarks>
        /// Tolerant of unreadable options on purpose. This is read while deciding what to run next,
        /// for every candidate task — so one row with options a newer build wrote, or a truncated
        /// write, used to throw out of the whole scheduling pass. That pass is what advances the
        /// queue, and it runs inside an event handler whose result nobody looks at, so the failure
        /// was invisible and the queue simply stopped. Falling back to defaults costs that one task
        /// its settings; throwing costs every task its turn.
        /// </remarks>
        public T GetTypedOptions<T>() where T : class, new()
        {
            if (string.IsNullOrEmpty(Options))
            {
                return new T();
            }

            try
            {
                return JsonSerializer.Deserialize<T>(Options, JsonSerializerOptions.Web) ?? new T();
            }
            catch (JsonException)
            {
                return new T();
            }
        }

        public void SetTypedOptions<T>(T options) where T : class
        {
            Options = JsonSerializer.Serialize(options, JsonSerializerOptions.Web);
        }

        [NotMapped] public string DisplayName => Name ?? Key;

        public bool CanStart => AvailableActions.Contains(DownloadTaskAction.StartManually) ||
                                AvailableActions.Contains(DownloadTaskAction.Restart) ||
                                AvailableActions.Contains(DownloadTaskAction.StartAutomatically);
    }
}