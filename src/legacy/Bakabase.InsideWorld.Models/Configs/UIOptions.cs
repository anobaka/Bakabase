using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs
{
    [Options]
    public class UIOptions
    {
        public UIResourceOptions Resource { get; set; } = new UIResourceOptions();
        public StartupPage StartupPage { get; set; }

        public bool IsMenuCollapsed { get; set; }

        public bool HideResourceCovers { get; set; }

        public ResourceDetailLayoutConfig? ResourceDetailLayout { get; set; }

        public List<PropertyKey> LatestUsedProperties { get; set; } = new List<PropertyKey>();

        /// <summary>
        /// The notices shipped with the app, which the UI shows once at startup until they are
        /// read. Kept per install rather than per browser, so every window and browser showing
        /// this install agrees on what has been read.
        /// </summary>
        public UINoticeOptions Notices { get; set; } = new();

        public void AddLatestUsedProperty(int pool, int id)
        {
            var key = new PropertyKey { Pool = pool, Id = id };
            
            // Remove if already exists
            LatestUsedProperties.RemoveAll(p => p.Pool == pool && p.Id == id);
            
            // Add to beginning
            LatestUsedProperties.Insert(0, key);
            
            // Keep only latest 5
            if (LatestUsedProperties.Count > 5)
            {
                LatestUsedProperties = LatestUsedProperties.Take(5).ToList();
            }
        }

        public record PropertyKey
        {
            public int Pool { get; set; }
            public int Id { get; set; }
        }

        /// <summary>
        /// What this install knows about the notices defined by the UI. The notices themselves
        /// (their text, audience and which of them are upgrade-only) live in the UI's code; the
        /// server only keeps ids, so it never needs to change when a notice is added.
        /// </summary>
        public record UINoticeOptions
        {
            /// <summary>The UI's ids are short, stable slugs; anything longer is not one.</summary>
            public const int MaxIdLength = 128;

            /// <summary>
            /// Far more notices than will ever ship, and small enough that no caller can grow
            /// the options file without bound.
            /// </summary>
            public const int MaxReadIds = 1024;

            /// <summary>
            /// Ids of the notices read on this install. Only ever grows. An id the running UI
            /// does not know is kept, not pruned: a notice read in a newer build must still be
            /// read after going back to an older one.
            /// </summary>
            public List<string> ReadIds { get; set; } = [];

            /// <summary>
            /// True from a fresh install's first start until its UI has recorded the
            /// upgrade-only notices it shipped with as read (<see cref="CaptureBaseline"/>).
            /// Those notices describe a change from a version this install never ran, so it
            /// must not be greeted with them; notices added in later releases are not in that
            /// baseline and reach it like any other install. An install that existed before
            /// this field did starts with it false, and so sees them.
            /// </summary>
            public bool BaselinePending { get; set; }

            /// <summary>
            /// Records <paramref name="ids"/> as read. Idempotent, and tolerant of anything a
            /// caller sends: blank ids, ids over <see cref="MaxIdLength"/> and ids past
            /// <see cref="MaxReadIds"/> are ignored; unknown ids are kept.
            /// </summary>
            /// <returns>Whether anything was added.</returns>
            public bool MarkRead(IEnumerable<string?>? ids)
            {
                if (ids == null)
                {
                    return false;
                }

                ReadIds ??= [];
                var known = new HashSet<string>(ReadIds, StringComparer.Ordinal);
                var changed = false;

                foreach (var raw in ids)
                {
                    var id = raw?.Trim();
                    if (string.IsNullOrEmpty(id) || id.Length > MaxIdLength || ReadIds.Count >= MaxReadIds)
                    {
                        continue;
                    }

                    if (known.Add(id))
                    {
                        ReadIds.Add(id);
                        changed = true;
                    }
                }

                return changed;
            }

            /// <summary>
            /// Called once by a fresh install's UI with the upgrade-only notices it ships:
            /// they are recorded as read and the baseline is closed. Does nothing once it is
            /// closed — for an install that was not fresh it never opened — so a later build
            /// cannot use it to hide the notices it adds.
            /// </summary>
            /// <returns>Whether the baseline was open.</returns>
            public bool CaptureBaseline(IEnumerable<string?>? ids)
            {
                if (!BaselinePending)
                {
                    return false;
                }

                MarkRead(ids);
                BaselinePending = false;
                return true;
            }
        }

        public record UIResourceOptions
        {
            public int ColCount { get; set; }
            public bool ShowBiggerCoverWhileHover { get; set; }
            public bool DisableMediaPreviewer { get; set; }
            public bool DisableCoverCache { get; set; }
            public bool DisablePlayableFileCache { get; set; }
            public CoverFit CoverFit { get; set; } = CoverFit.Contain;
            public bool DisableCoverCarousel { get; set; }
            public bool DisplayResourceId { get; set; }
            public bool HideResourceTimeInfo { get; set; }
            public List<PropertyKey> DisplayProperties { get; set; } = [];
            public bool InlineDisplayName { get; set; }
            public bool AutoSelectFirstPlayableFile { get; set; }
            public List<string> DisplayOperations { get; set; } = [];
            public bool HideResourceBorder { get; set; }
            public bool HideHealthScore { get; set; }
            public List<CustomContextMenuItem> CustomContextMenuItems { get; set; } = [];
            /// <summary>
            /// When true, manually saved property values in resource detail modal
            /// are automatically added to the quick-set context menu config.
            /// </summary>
            public bool AutoAddRecentPropertyValues { get; set; }
        }

        public record CustomContextMenuItem
        {
            public PropertyKey Property { get; set; }
            /// <summary>
            /// Serialized DB values for preset quick-access values.
            /// For reference types, these are UUIDs from property options.
            /// For other types, these are serialized standard values.
            /// </summary>
            public List<string> PresetValues { get; set; } = [];
        }

        public record ResourceDetailLayoutConfig
        {
            public int ModalWidthPercent { get; set; }
            public int ModalHeightPercent { get; set; }
            public int GridCols { get; set; }
            public int Gap { get; set; }
            public List<ResourceDetailBlock> Blocks { get; set; } = [];
            public List<ResourceDetailBlock> Hidden { get; set; } = [];
        }

        public record ResourceDetailBlock
        {
            public string Id { get; set; } = string.Empty;
            public int ColStart { get; set; }
            public int ColSpan { get; set; }
            public int RowStart { get; set; }
            public int RowSpan { get; set; }
        }
    }
}