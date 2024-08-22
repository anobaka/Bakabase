﻿using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Abstractions.Models.Input
{
    public class CategoryPatchInputModel
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Color { get; set; }
        public CoverSelectOrder? CoverSelectionOrder { get; set; }
        public int? Order { get; set; }
        public bool? GenerateNfo { get; set; }
        public string? ResourceDisplayNameTemplate { get; set; }
    }
}