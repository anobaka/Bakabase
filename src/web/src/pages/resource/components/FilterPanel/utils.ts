import type { SearchForm } from "@/pages/resource/models.ts";
import {
  GroupCombinator,
  type ResourceSearchFilter,
  type ResourceSearchFilterGroup,
} from "@/pages/resource/components/FilterPanel/FilterGroupsPanel/models.ts";

export const addFilterGroup = (
  form: SearchForm,
  group?: ResourceSearchFilterGroup,
) => {
  if (!group) {
    // Create a new default filter group if none provided
    group = {
      combinator: GroupCombinator.And, // GroupCombinator.And
      disabled: false,
      filters: [],
      groups: [],
    };
  }

  // If form doesn't have a group yet, set it directly
  if (!form.group) {
    form.group = group;
  } else {
    // If form already has a group, add the new group as a child
    if (!form.group.groups) {
      form.group.groups = [];
    }
    form.group.groups.push(group);
  }

  return form;
};

export const addFilter = (
  form: SearchForm,
  filter?: ResourceSearchFilter,
) => {
  if (!filter) {
    // Create a new default filter if none provided
    filter = {
      disabled: false,
      availableOperations: [],
    };
  }

  // Ensure form has a group to add the filter to
  if (!form.group) {
    form.group = {
      combinator: GroupCombinator.And, // GroupCombinator.And
      disabled: false,
      filters: [],
      groups: [],
    };
  }

  // Add the filter to the group
  if (!form.group.filters) {
    form.group.filters = [];
  }
  form.group.filters.push(filter);

  return form;
};
