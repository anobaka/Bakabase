"use client";

import type { SearchFilter, SearchFilterGroup } from "@/components/ResourceFilter/models";
import type { SearchForm } from "@/pages/resource/models";

import React from "react";
import { useTranslation } from "react-i18next";
import { MdOutlineFilterAltOff } from "react-icons/md";

import { GroupCombinator } from "@/components/ResourceFilter/models";
import { getOperationDisplay } from "@/components/ResourceFilter/components/Filter/utils";
import { filterValueToText } from "@/pages/resource/utils/filterValueToText";
import { groupHasContent, hasSearchSummary } from "@/pages/resource/utils/searchSummary";
import { getEnumKey } from "@/i18n";
import { resourceSearchSortableProperties, resourceTags, SearchOperation } from "@/sdk/constants";

export { hasSearchSummary };

// Keeps a single value from blowing the tooltip up when someone filters on a
// long path or a dozen tags.
const MaxValueLength = 80;
// Same idea for the row count: past a certain depth the summary stops being a
// glance and becomes a second filter panel.
const MaxRowsPerGroup = 10;

const truncate = (text: string) =>
  text.length > MaxValueLength ? `${text.slice(0, MaxValueLength)}…` : text;

/**
 * One filter rendered as `property operator value`. The three parts are
 * colored apart so the eye can parse a stack of them without reading:
 * property = foreground, operator = secondary, value = primary — the same
 * roles the filter panel itself uses.
 */
const FilterRow = ({ filter }: { filter: SearchFilter }) => {
  const { t } = useTranslation();

  const name = filter.property?.name ?? t<string>("resource.tab.summary.unknownProperty");
  const operation =
    filter.operation == undefined
      ? undefined
      : getOperationDisplay(filter.operation, filter.property?.type, t);
  // IsNull / IsNotNull carry no value by design.
  const valueless =
    filter.operation === SearchOperation.IsNull || filter.operation === SearchOperation.IsNotNull;
  const value = valueless ? "" : filterValueToText(filter);

  return (
    <div
      className={`flex items-baseline gap-1 min-w-0 ${filter.disabled ? "opacity-40 line-through" : ""}`}
    >
      {filter.disabled && (
        <MdOutlineFilterAltOff className="shrink-0 self-center text-warning text-[13px]" />
      )}
      <span className="shrink-0 font-medium text-foreground">{name}</span>
      {operation && <span className="shrink-0 text-secondary">{operation}</span>}
      {value && <span className="text-primary break-all line-clamp-2">{truncate(value)}</span>}
    </div>
  );
};

FilterRow.displayName = "FilterRow";

/**
 * A filter group. Rows after the first are prefixed by the group's combinator
 * in a fixed gutter, so the property names of sibling filters stay aligned.
 * Nested groups get a left rule instead of parentheses.
 */
const GroupNode = ({ group, depth = 0 }: { group: SearchFilterGroup; depth?: number }) => {
  const { t } = useTranslation();

  const children: React.ReactNode[] = [
    ...(group.filters ?? []).map((f, i) => <FilterRow key={`f-${i}`} filter={f} />),
    ...(group.groups ?? [])
      .filter(groupHasContent)
      .map((g, i) => <GroupNode key={`g-${i}`} depth={depth + 1} group={g} />),
  ];

  const overflow = children.length - MaxRowsPerGroup;
  const visible = overflow > 0 ? children.slice(0, MaxRowsPerGroup) : children;
  const combinator = t<string>(getEnumKey("Combinator", GroupCombinator[group.combinator]));
  // A lone filter needs no combinator, so it also needs no gutter — which is
  // the common case and the one worth keeping tight.
  const showCombinator = visible.length > 1;

  return (
    <div
      className={`flex flex-col gap-0.5 min-w-0 ${group.disabled ? "opacity-40" : ""} ${
        depth > 0 ? "pl-1.5 border-l border-default-300" : ""
      }`}
    >
      {visible.map((child, i) => (
        <div key={i} className="flex items-baseline gap-1 min-w-0">
          {showCombinator && (
            <span className="shrink-0 w-7 text-right text-[10px] text-default-400">
              {i > 0 ? combinator : ""}
            </span>
          )}
          <div className="min-w-0">{child}</div>
        </div>
      ))}
      {overflow > 0 && (
        <div className={`text-[10px] text-default-400 ${showCombinator ? "pl-8" : ""}`}>
          {t<string>("resource.tab.summary.more", { count: overflow })}
        </div>
      )}
    </div>
  );
};

GroupNode.displayName = "GroupNode";

const Section = ({ label, children }: { label: string; children: React.ReactNode }) => (
  <>
    <div className="text-default-400 whitespace-nowrap">{label}</div>
    <div className="min-w-0">{children}</div>
  </>
);

/**
 * Compact, read-only digest of a tab's search criteria — keyword, filters,
 * resource tags and ordering — for the tab tooltip.
 */
const SearchSummary = ({ form }: { form: SearchForm | undefined }) => {
  const { t } = useTranslation();

  if (!form || !hasSearchSummary(form)) {
    return null;
  }

  const { group } = form;

  return (
    <div className="grid grid-cols-[auto_1fr] items-baseline gap-x-2 gap-y-1 max-w-[400px] py-1 text-xs">
      {form.keyword && (
        <Section label={t<string>("resource.tab.summary.keyword")}>
          <span className="text-primary break-all line-clamp-2">{truncate(form.keyword)}</span>
        </Section>
      )}
      {group && groupHasContent(group) && (
        <Section label={t<string>("resource.tab.summary.filters")}>
          <GroupNode group={group} />
        </Section>
      )}
      {!!form.tags?.length && (
        <Section label={t<string>("resource.tab.summary.tags")}>
          <div className="flex flex-wrap gap-1">
            {form.tags.map((tag) => {
              const label = resourceTags.find((rt) => rt.value === tag)?.label;

              return (
                <span key={tag} className="px-1 rounded bg-default-100 text-primary">
                  {label ? t<string>(getEnumKey("ResourceTag", label)) : tag}
                </span>
              );
            })}
          </div>
        </Section>
      )}
      {!!form.orders?.length && (
        <Section label={t<string>("resource.tab.summary.order")}>
          <div className="flex flex-wrap items-center gap-x-2 gap-y-0.5">
            {form.orders.map((order, i) => {
              const label = resourceSearchSortableProperties.find(
                (p) => p.value === order.property,
              )?.label;

              return (
                <span key={i} className="flex items-baseline gap-1">
                  <span className="font-medium text-foreground">
                    {label ? t<string>(`ResourceSearchSortableProperty.${label}`) : order.property}
                  </span>
                  {/* Direction is the "how" of an order, the same role an
                      operator plays for a filter — so it takes that color. */}
                  <span className="text-secondary">
                    {t<string>(order.asc ? "resource.order.asc" : "resource.order.desc")}
                  </span>
                </span>
              );
            })}
          </div>
        </Section>
      )}
    </div>
  );
};

SearchSummary.displayName = "SearchSummary";

export default SearchSummary;
