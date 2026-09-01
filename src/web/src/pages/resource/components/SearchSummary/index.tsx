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

type GroupChild =
  | { kind: "filter"; filter: SearchFilter }
  | { kind: "group"; group: SearchFilterGroup };

/** A group's rows, in the order they read: own filters first, then subgroups. */
const childrenOf = (group: SearchFilterGroup): GroupChild[] => [
  ...(group.filters ?? []).map((filter) => ({ kind: "filter" as const, filter })),
  ...(group.groups ?? [])
    .filter(groupHasContent)
    .map((child) => ({ kind: "group" as const, group: child })),
];

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
      <span className="shrink-0 font-medium text-foreground">{name}</span>
      {operation && <span className="shrink-0 text-secondary">{operation}</span>}
      {value && <span className="text-primary break-all line-clamp-2">{truncate(value)}</span>}
      {/* Trailing, so a disabled row's property name still lines up with the
          rest of the column. */}
      {filter.disabled && (
        <MdOutlineFilterAltOff className="shrink-0 self-center text-warning text-[13px]" />
      )}
    </div>
  );
};

FilterRow.displayName = "FilterRow";

/**
 * A nested group. Its combinator goes inline ahead of each row after the
 * first, and a left rule stands in for the parentheses.
 */
const GroupNode = ({ group }: { group: SearchFilterGroup }): React.ReactElement => {
  const { t } = useTranslation();

  const children = childrenOf(group);
  const overflow = children.length - MaxRowsPerGroup;
  const visible = overflow > 0 ? children.slice(0, MaxRowsPerGroup) : children;
  const combinator = t<string>(getEnumKey("Combinator", GroupCombinator[group.combinator]));

  return (
    <div
      className={`flex flex-col gap-0.5 min-w-0 pl-1.5 border-l border-default-300 ${
        group.disabled ? "opacity-40" : ""
      }`}
    >
      {visible.map((child, i) => (
        <div key={i} className="flex items-baseline gap-1 min-w-0">
          {i > 0 && <span className="shrink-0 text-[10px] text-default-400">{combinator}</span>}
          <div className="min-w-0">
            {child.kind === "filter" ? (
              <FilterRow filter={child.filter} />
            ) : (
              <GroupNode group={child.group} />
            )}
          </div>
        </div>
      ))}
      {overflow > 0 && (
        <div className="text-[10px] text-default-400">
          {t<string>("resource.tab.summary.more", { count: overflow })}
        </div>
      )}
    </div>
  );
};

GroupNode.displayName = "GroupNode";

/**
 * One line of the summary. The left column is a rail of section labels and, for
 * the filter rows, the combinator joining them — which keeps every value in the
 * right column flush against the same edge.
 */
const Row = ({ label, children }: { label?: string; children: React.ReactNode }) => (
  <>
    <div className="text-right text-default-400 whitespace-nowrap">{label}</div>
    <div className="min-w-0">{children}</div>
  </>
);

Row.displayName = "Row";

type Props = {
  form: SearchForm | undefined;
  /** The tab isn't mounted, so its criteria are still being fetched. */
  loading?: boolean;
};

/**
 * Compact, read-only digest of a tab's search criteria — keyword, filters,
 * resource tags and ordering — for the tab tooltip.
 */
const SearchSummary = ({ form, loading }: Props) => {
  const { t } = useTranslation();

  if (loading) {
    return <div className="py-1 text-xs text-default-400">{t<string>("common.state.loading")}</div>;
  }

  if (!form || !hasSearchSummary(form)) {
    return (
      <div className="py-1 text-xs text-default-400">{t<string>("resource.tab.summary.empty")}</div>
    );
  }

  const { group } = form;

  // The root group is flattened into the grid rather than nested inside one
  // cell, so its combinator sits in the label column and its filters share the
  // content column with the keyword, tags and ordering.
  const rootChildren = group && groupHasContent(group) ? childrenOf(group) : [];
  const rootOverflow = rootChildren.length - MaxRowsPerGroup;
  const visibleRootChildren =
    rootOverflow > 0 ? rootChildren.slice(0, MaxRowsPerGroup) : rootChildren;
  const rootCombinator = group
    ? t<string>(getEnumKey("Combinator", GroupCombinator[group.combinator]))
    : "";

  return (
    <div className="grid grid-cols-[auto_1fr] items-baseline gap-x-2 gap-y-1 max-w-[400px] py-1 text-xs">
      {form.keyword && (
        <Row label={t<string>("resource.tab.summary.keyword")}>
          <span className="text-primary break-all line-clamp-2">{truncate(form.keyword)}</span>
        </Row>
      )}
      {visibleRootChildren.map((child, i) => (
        <Row key={i} label={i === 0 ? t<string>("resource.tab.summary.filters") : rootCombinator}>
          <div className={group?.disabled ? "opacity-40" : ""}>
            {child.kind === "filter" ? (
              <FilterRow filter={child.filter} />
            ) : (
              <GroupNode group={child.group} />
            )}
          </div>
        </Row>
      ))}
      {rootOverflow > 0 && (
        <Row>
          <span className="text-[10px] text-default-400">
            {t<string>("resource.tab.summary.more", { count: rootOverflow })}
          </span>
        </Row>
      )}
      {!!form.tags?.length && (
        <Row label={t<string>("resource.tab.summary.tags")}>
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
        </Row>
      )}
      {!!form.orders?.length && (
        <Row label={t<string>("resource.tab.summary.order")}>
          <div className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
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
        </Row>
      )}
    </div>
  );
};

SearchSummary.displayName = "SearchSummary";

export default SearchSummary;
