import type { SearchForm } from "@/pages/resource/models.ts";
import type { SearchFilter, SearchFilterGroup } from "@/components/ResourceFilter";

import { GroupCombinator } from "@/components/ResourceFilter/models";

/** A restored query must show every grouping and disabled-state control it uses. */
export const requiresAdvancedFilterMode = (group?: SearchFilterGroup): boolean =>
  !!group &&
  (group.combinator === GroupCombinator.Or ||
    group.disabled ||
    !!group.groups?.length ||
    !!group.filters?.some((filter) => filter.disabled));

export const addFilterGroup = (form: SearchForm, group?: SearchFilterGroup) => {
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

export const addFilter = (form: SearchForm, filter?: SearchFilter) => {
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

  return filter;
};
