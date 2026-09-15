"use client";

"use strict";

import type { SearchFilter, SearchFilterGroup } from "../../models";
import type { FilterLayout } from "../Filter";

import { useTranslation } from "react-i18next";
import React, { useCallback, useRef, useState } from "react";
import { useUpdateEffect } from "react-use";
import { DeleteOutlined, MoreOutlined } from "@ant-design/icons";
import { TbFilterPlus } from "react-icons/tb";
import { MdOutlineFilterAlt, MdOutlineFilterAltOff } from "react-icons/md";

import { GroupCombinator } from "../../models";
import Filter from "../Filter";
import FilterAddPopoverContent from "../FilterAddPopoverContent";

import { FilterDisplayMode } from "@/sdk/constants";
import { Button, Popover } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";

type Props = {
  group: SearchFilterGroup;
  onRemove?: () => void;
  onChange?: (group: SearchFilterGroup) => void;
  isRoot?: boolean;
  /** Index of filter that should auto-trigger property selector (set from external) */
  externalNewFilterIndex?: number | null;
  /** Called when external new filter index is consumed */
  onExternalNewFilterConsumed?: () => void;
  /** Filter display mode - Simple hides operation selector */
  filterDisplayMode?: FilterDisplayMode;
  /** Filter layout - horizontal or vertical */
  filterLayout?: FilterLayout;
  /** Readonly mode - hides all action buttons and makes filters readonly */
  isReadonly?: boolean;
};

const log = buildLogger("FilterGroup");

// Stable per-filter React keys. Index-based keys cause Filter component reuse
// across positions when filters are added/removed, which leaks stale internal
// state (e.g. value display). Lazy-attaching a key to each filter on first
// render gives every filter a stable identity that survives spread-edits.
let __filterKeySeq = 0;
const getStableFilterKey = (f: SearchFilter): string => {
  const anyF = f as SearchFilter & { __clientKey?: string };

  if (!anyF.__clientKey) {
    anyF.__clientKey = `__fk${++__filterKeySeq}`;
  }

  return anyF.__clientKey;
};

let __groupKeySeq = 0;
const getStableGroupKey = (g: SearchFilterGroup): string => {
  const anyG = g as SearchFilterGroup & { __clientKey?: string };

  if (!anyG.__clientKey) {
    anyG.__clientKey = `__gk${++__groupKeySeq}`;
  }

  return anyG.__clientKey;
};

const FilterGroup = ({
  group: propsGroup,
  onRemove,
  onChange,
  isRoot = false,
  externalNewFilterIndex,
  onExternalNewFilterConsumed,
  filterDisplayMode,
  filterLayout,
  isReadonly = false,
}: Props) => {
  const { t } = useTranslation();

  const [group, setGroup] = React.useState<SearchFilterGroup>(propsGroup);
  const groupRef = useRef(group);
  const [popoverOpen, setPopoverOpen] = useState(false);
  const [actionsPopoverOpen, setActionsPopoverOpen] = useState(false);
  // Track newly added filter index to auto-trigger property selector
  const [internalNewFilterIndex, setInternalNewFilterIndex] = useState<number | null>(null);

  // Combine internal and external new filter index
  const newFilterIndex = internalNewFilterIndex ?? externalNewFilterIndex ?? null;

  const changeGroup = useCallback(
    (newGroup: SearchFilterGroup) => {
      setGroup(newGroup);
      onChange?.(newGroup);
    },
    [onChange],
  );

  useUpdateEffect(() => {
    groupRef.current = group;
  }, [group]);

  useUpdateEffect(() => {
    setGroup(propsGroup);
  }, [propsGroup]);

  const { filters, groups, combinator } = group;
  const isSimpleMode = filterDisplayMode === FilterDisplayMode.Simple;
  const isVerticalLayout = filterLayout === "vertical";

  log("FilterGroup render", {
    isRoot,
    filtersCount: filters?.length,
    groupsCount: groups?.length,
    combinator,
  });

  const renderCombinator = () => {
    if (isSimpleMode) return null;
    const isAnd = combinator === GroupCombinator.And;
    const label = t<string>(
      isAnd ? "resourceFilter.group.matchAll" : "resourceFilter.group.matchAny",
    );

    if (isReadonly) {
      return (
        <span className="inline-flex min-h-8 items-center text-xs font-medium text-default-600">
          {label}
        </span>
      );
    }

    return (
      <Button
        aria-label={t<string>("resourceFilter.group.changeLogic", { logic: label })}
        className="h-8 min-w-0 px-2 text-xs font-medium text-default-600"
        size="sm"
        title={t<string>("resourceFilter.group.logicHint")}
        variant="flat"
        onPress={() =>
          changeGroup({ ...group, combinator: isAnd ? GroupCombinator.Or : GroupCombinator.And })
        }
      >
        {label}
      </Button>
    );
  };

  const conditionElements: any[] = (filters || [])
    .map((f, i) => {
      const isNewFilter = newFilterIndex === i;
      const filterElement = (
        <Filter
          key={getStableFilterKey(f)}
          autoTriggerPropertySelector={isNewFilter}
          filter={f}
          filterDisplayMode={filterDisplayMode}
          isReadonly={isReadonly}
          layout={filterLayout}
          onCancelNewFilter={
            isNewFilter
              ? () => {
                  setInternalNewFilterIndex(null);
                  onExternalNewFilterConsumed?.();
                  changeGroup({
                    ...groupRef.current,
                    filters: (groupRef.current.filters || []).filter((_, idx) => idx !== i),
                  });
                }
              : undefined
          }
          onChange={(tf) => {
            if (isNewFilter) {
              setInternalNewFilterIndex(null);
              onExternalNewFilterConsumed?.();
            }
            changeGroup({
              ...group,
              filters: (group.filters || []).map((fil) => (fil === f ? tf : fil)),
            });
          }}
          onRemove={() => {
            if (isNewFilter) {
              setInternalNewFilterIndex(null);
              onExternalNewFilterConsumed?.();
            }
            changeGroup({
              ...group,
              filters: (group.filters || []).filter((fil) => fil !== f),
            });
          }}
        />
      );

      return { element: filterElement, index: i };
    })
    .concat(
      (groups || []).map((g, i) => {
        const groupElement = (
          <FilterGroup
            key={getStableGroupKey(g)}
            filterDisplayMode={filterDisplayMode}
            filterLayout={filterLayout}
            group={g}
            isReadonly={isReadonly}
            onChange={(tg) => {
              changeGroup({
                ...group,
                groups: (group.groups || []).map((gr) => (gr === g ? tg : gr)),
              });
            }}
            onRemove={() => {
              changeGroup({
                ...group,
                groups: (group.groups || []).filter((gr) => gr !== g),
              });
            }}
          />
        );

        return { element: groupElement, index: (filters?.length || 0) + i };
      }),
    );

  // Group actions menu (delete/disable) - only for non-root groups
  const renderGroupActionsMenu = () => {
    if (isRoot || isReadonly) return null;

    return (
      <Popover
        isOpen={actionsPopoverOpen}
        placement="bottom-start"
        trigger={
          <Button
            isIconOnly
            aria-label={t<string>("resourceFilter.group.actions")}
            className="h-8 w-8 min-w-8 text-default-500"
            size="sm"
            variant="light"
          >
            <MoreOutlined className="text-base" />
          </Button>
        }
        onOpenChange={setActionsPopoverOpen}
      >
        <div className="flex flex-col gap-1 p-1">
          <Button
            className="justify-start"
            color="warning"
            size="sm"
            variant="light"
            onPress={() => {
              changeGroup({
                ...group,
                disabled: !group.disabled,
              });
              setActionsPopoverOpen(false);
            }}
          >
            {group.disabled ? (
              <>
                <MdOutlineFilterAlt className="text-lg" />
                {t<string>("resourceFilter.group.enable")}
              </>
            ) : (
              <>
                <MdOutlineFilterAltOff className="text-lg" />
                {t<string>("resourceFilter.group.disable")}
              </>
            )}
          </Button>
          <Button
            className="justify-start"
            color="danger"
            size="sm"
            variant="light"
            onPress={() => {
              setActionsPopoverOpen(false);
              onRemove?.();
            }}
          >
            <DeleteOutlined className="text-base" />
            {t<string>("resourceFilter.group.remove")}
          </Button>
        </div>
      </Popover>
    );
  };

  const renderGroup = () => {
    return (
      <div
        className={`flex min-w-0 max-w-full flex-col gap-2 text-sm ${isRoot ? "w-full" : "rounded-r-xl border-l-2 border-default-200/70 bg-default-50/50 p-2 pl-3"}`}
      >
        {(!isSimpleMode || !isRoot || group.disabled) && (
          <div className="flex min-w-0 flex-wrap items-center gap-2">
            {renderCombinator()}
            {isSimpleMode && !isRoot && (
              <span className="text-xs font-medium text-default-500">
                {t<string>("resourceFilter.group.label")}
              </span>
            )}
            {group.disabled && (
              <span className="text-xs text-default-400">
                {t<string>("resourceFilter.condition.disabled")}
              </span>
            )}
            <div className="ml-auto">{renderGroupActionsMenu()}</div>
          </div>
        )}
        <div
          className={`flex min-w-0 gap-2 ${isVerticalLayout ? "flex-col" : "flex-wrap items-start"} ${group.disabled ? "opacity-60" : ""}`}
        >
          {conditionElements.map((item) => item.element)}
        </div>
        {/* Hide add filter button in Simple mode - use FilterPortal instead */}
        {!isSimpleMode &&
          !isReadonly &&
          !(isRoot && !group && (!filters || filters.length == 0)) && (
            <Popover
              showArrow
              isOpen={popoverOpen}
              placement={"bottom"}
              trigger={
                <Button
                  aria-label={t<string>("resourceFilter.group.addCondition")}
                  className="h-8 min-w-0 self-start text-default-600"
                  size="sm"
                  startContent={<TbFilterPlus aria-hidden className="text-base" />}
                  variant="light"
                >
                  {t<string>("resourceFilter.group.addCondition")}
                </Button>
              }
              onOpenChange={setPopoverOpen}
            >
              <FilterAddPopoverContent
                showRecentFilters
                showTags={false}
                onAddFilter={(autoTrigger) => {
                  const currentFilters = groupRef.current.filters || [];

                  if (autoTrigger) {
                    setInternalNewFilterIndex(currentFilters.length);
                  }
                  changeGroup({
                    ...groupRef.current,
                    filters: [...currentFilters, { disabled: false }],
                  });
                }}
                onAddFilterGroup={() => {
                  changeGroup({
                    ...groupRef.current,
                    groups: [
                      ...(groupRef.current.groups || []),
                      {
                        combinator: GroupCombinator.And,
                        disabled: false,
                      },
                    ],
                  });
                }}
                onClose={() => setPopoverOpen(false)}
                onSelectFilters={(filters) => {
                  changeGroup({
                    ...groupRef.current,
                    filters: [...(groupRef.current.filters || []), ...filters],
                  });
                }}
              />
            </Popover>
          )}
      </div>
    );
  };

  return renderGroup();
};

export default FilterGroup;
