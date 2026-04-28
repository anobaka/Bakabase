using System.Collections.Generic;

namespace Bakabase.Service.Models.View;

public record ResourceHierarchyContextViewModel(
    List<ResourceAncestorViewModel> Ancestors,
    int? ChildrenCount);

public record ResourceAncestorViewModel(
    int Id,
    string DisplayName,
    int? ParentId);
