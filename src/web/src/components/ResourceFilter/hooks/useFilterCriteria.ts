"use client";

import type { SearchFilter, SearchFilterGroup } from "../models";
import type { ResourceTag } from "@/sdk/constants";

import { useState, useCallback } from "react";

import { GroupCombinator } from "../models";

export interface SearchCriteria {
  group?: SearchFilterGroup;
  keyword?: string;
  tags?: ResourceTag[];
}

export interface UseFilterCriteriaOptions {
  initialCriteria?: SearchCriteria;
  onChange?: (criteria: SearchCriteria) => void;
}

export interface UseFilterCriteriaReturn {
  criteria: SearchCriteria;
  setCriteria: (criteria: SearchCriteria) => void;
  setKeyword: (keyword: string | undefined) => void;
  setTags: (tags: ResourceTag[]) => void;
  setGroup: (group: SearchFilterGroup | undefined) => void;
  addFilter: (filter: SearchFilter, autoTriggerPropertySelector?: boolean) => number;
  removeFilter: (index: number) => void;
  updateFilter: (index: number, filter: SearchFilter) => void;
  addFilterGroup: () => void;
  removeFilterGroup: (index: number) => void;
  updateFilterGroup: (index: number, group: SearchFilterGroup) => void;
  clearAll: () => void;
  hasFilters: boolean;
}

const createDefaultGroup = (): SearchFilterGroup => ({
  combinator: GroupCombinator.And,
  disabled: false,
  filters: [],
  groups: [],
});

export function useFilterCriteria(options: UseFilterCriteriaOptions = {}): UseFilterCriteriaReturn {
  const { initialCriteria, onChange } = options;
  const [criteria, setCriteriaInternal] = useState<SearchCriteria>(initialCriteria ?? {});

  const setCriteria = useCallback((newCriteria: SearchCriteria) => {
    setCriteriaInternal(newCriteria);
    onChange?.(newCriteria);
  }, [onChange]);

  const setKeyword = useCallback((keyword: string | undefined) => {
    setCriteria({ ...criteria, keyword });
  }, [criteria, setCriteria]);

  const setTags = useCallback((tags: ResourceTag[]) => {
    setCriteria({
      ...criteria,
      tags: tags.length > 0 ? tags : undefined,
    });
  }, [criteria, setCriteria]);

  const setGroup = useCallback((group: SearchFilterGroup | undefined) => {
    setCriteria({ ...criteria, group });
  }, [criteria, setCriteria]);

  const addFilter = useCallback((filter: SearchFilter, autoTriggerPropertySelector = false): number => {
    const newGroup: SearchFilterGroup = criteria.group ?? createDefaultGroup();

    if (!newGroup.filters) {
      newGroup.filters = [];
    }

    const newIndex = newGroup.filters.length;
    newGroup.filters.push(filter);

    setCriteria({ ...criteria, group: { ...newGroup } });
    return newIndex;
  }, [criteria, setCriteria]);

  const removeFilter = useCallback((index: number) => {
    if (!criteria.group?.filters) return;

    const newFilters = [...criteria.group.filters];
    newFilters.splice(index, 1);

    setCriteria({
      ...criteria,
      group: {
        ...criteria.group,
        filters: newFilters,
      },
    });
  }, [criteria, setCriteria]);

  const updateFilter = useCallback((index: number, filter: SearchFilter) => {
    if (!criteria.group?.filters) return;

    const newFilters = [...criteria.group.filters];
    newFilters[index] = filter;

    setCriteria({
      ...criteria,
      group: {
        ...criteria.group,
        filters: newFilters,
      },
    });
  }, [criteria, setCriteria]);

  const addFilterGroup = useCallback(() => {
    const newSubGroup: SearchFilterGroup = createDefaultGroup();
    const newGroup: SearchFilterGroup = criteria.group ?? createDefaultGroup();

    if (!newGroup.groups) {
      newGroup.groups = [];
    }
    newGroup.groups.push(newSubGroup);

    setCriteria({ ...criteria, group: { ...newGroup } });
  }, [criteria, setCriteria]);

  const removeFilterGroup = useCallback((index: number) => {
    if (!criteria.group?.groups) return;

    const newGroups = [...criteria.group.groups];
    newGroups.splice(index, 1);

    setCriteria({
      ...criteria,
      group: {
        ...criteria.group,
        groups: newGroups,
      },
    });
  }, [criteria, setCriteria]);

  const updateFilterGroup = useCallback((index: number, group: SearchFilterGroup) => {
    if (!criteria.group?.groups) return;

    const newGroups = [...criteria.group.groups];
    newGroups[index] = group;

    setCriteria({
      ...criteria,
      group: {
        ...criteria.group,
        groups: newGroups,
      },
    });
  }, [criteria, setCriteria]);

  const clearAll = useCallback(() => {
    setCriteria({});
  }, [setCriteria]);

  const hasFilters =
    (criteria.group?.filters && criteria.group.filters.length > 0) ||
    (criteria.group?.groups && criteria.group.groups.length > 0) ||
    false;

  return {
    criteria,
    setCriteria,
    setKeyword,
    setTags,
    setGroup,
    addFilter,
    removeFilter,
    updateFilter,
    addFilterGroup,
    removeFilterGroup,
    updateFilterGroup,
    clearAll,
    hasFilters,
  };
}

export default useFilterCriteria;
