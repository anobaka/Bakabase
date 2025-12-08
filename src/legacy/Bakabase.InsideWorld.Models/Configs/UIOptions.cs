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
        public UIResourceOptions Resource { get; set; }
        public StartupPage StartupPage { get; set; }
        
        public bool IsMenuCollapsed { get; set; }
        
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
            public bool DisableCache { get; set; }
            public CoverFit CoverFit { get; set; } = CoverFit.Contain;
            public bool DisableCoverCarousel { get; set; }
            public bool DisplayResourceId { get; set; }
            public bool HideResourceTimeInfo { get; set; }
            public List<PropertyKey> DisplayProperties { get; set; } = [];
            public bool InlineDisplayName { get; set; }
            public bool AutoSelectFirstPlayableFile { get; set; }
            public List<string> DisplayOperations { get; set; } = [];
        }
    }
}