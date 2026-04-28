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