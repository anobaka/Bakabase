using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models
{
    public sealed record DownloadFileVerification(string Stage, int? WorkflowRunId, DateTimeOffset ObservedAt)
    {
        public const string Source = "VerifyingSource";
        public const string Local = "VerifyingLocalFiles";
    }

    public class DownloadTask
    {
        // Bound to this worker snapshot only. A queued start, process restart or new
        // operator action gets a fresh snapshot with no prepared-download permission.
        private string? _preparedRetryOperationId;
        private bool _handoffPreparedPayloadOnly;
        private int _preparedRetryClaimed;
        internal void BindPreparedRetry(string operationId, bool handoffPreparedPayloadOnly = false)
        {
            _preparedRetryOperationId = operationId;
            _handoffPreparedPayloadOnly = handoffPreparedPayloadOnly;
        }
        public bool HasPreparedRetry() => _preparedRetryOperationId != null;
        public bool IsPreparedPayloadHandoffOnly() => HasPreparedRetry() && _handoffPreparedPayloadOnly;
        public bool TryConsumePreparedRetry(string operationId) =>
            _preparedRetryOperationId != null && _preparedRetryOperationId == operationId &&
            System.Threading.Interlocked.Exchange(ref _preparedRetryClaimed, 1) == 0;

        public int Id { get; set; }
        public string Key { get; set; } = null!;

        /// <summary>
        /// Populated during downloading
        /// </summary>
        public string? Name { get; set; }

        public ThirdPartyId ThirdPartyId { get; set; }
        public int Type { get; set; }
        public decimal Progress { get; set; }
        /// <summary>Total bytes in files attributed to this task; null until a file is observed.</summary>
        public long? DownloadedBytes { get; set; }
        /// <summary>Live operation bytes, including partial files; never added to DownloadedBytes.</summary>
        public long? TransferredBytes { get; set; }
        /// <summary>Known total for the current transfer operation, otherwise null.</summary>
        public long? TotalBytes { get; set; }
        public DateTime? TransferUpdatedAt { get; set; }
        /// <summary>
        /// Recent bytes per second received for this task's active media transfer; null when
        /// no reliable live byte source is available. Transient, never persisted.
        /// </summary>
        public double? DownloadSpeedBytesPerSecond { get; set; }
        /// <summary>
        /// Estimated seconds remaining in the current run; null when not downloading or when
        /// there is not enough recent progress to estimate. Transient, never persisted.
        /// </summary>
        public double? EstimatedRemainingSeconds { get; set; }
        public DateTime DownloadStatusUpdateDt { get; set; }
        public long? Interval { get; set; }
        public int? StartPage { get; set; }
        public int? EndPage { get; set; }
        public string? Message { get; set; }
        public string? Checkpoint { get; set; }
        public DownloadTaskStatus Status { get; set; }
        public string DownloadPath { get; set; } = null!;
        public string? Current { get; set; }
        /// <summary>The current read-only check of an existing download, independent of its persisted status.</summary>
        public DownloadFileVerification? FileVerification { get; set; }
        public int FailureTimes { get; set; }
        public bool AutoRetry { get; set; }
        public DateTime? NextStartDt { get; set; }
        public HashSet<DownloadTaskAction> AvailableActions { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        /// <summary>UTC time of the most recent successful run; failures do not erase this history.</summary>
        public DateTime? CompletedAt { get; set; }
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
