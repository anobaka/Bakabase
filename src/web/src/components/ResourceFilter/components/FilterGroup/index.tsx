"use client";

"use strict";

import type { SearchFilterGroup } from "../../models";
import type { FilterLayout } from "../Filter";

import { FilterDisplayMode } from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import React, { useCallback, useRef, useState } from "react";
import { useUpdateEffect } from "react-use";
import { DeleteOutlined, MoreOutlined } from "@ant-design/icons";
import { TbFilterPlus } from "react-icons/tb";
import { MdOutlineFilterAlt, MdOutlineFilterAltOff } from "react-icons/md";

import { GroupCombinator } from "../../models";
import Filter from "../Filter";
import FilterAddPopoverContent from "../FilterAddPopoverContent";

import { Button, Popover } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";
import { getEnumKey } from "@/i18n";

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

  log("FilterGroup render", { isRoot, filtersCount: filters?.length, groupsCount: groups?.length, combinator });

  // Combinator button/text component
  const renderCombinator = (index: number) => {
    if (isSimpleMode) return null;

    const combinatorText = t<string>(getEnumKey('Combinator', GroupCombinator[combinator]));
    const isAnd = combinator === GroupCombinator.And;

    const handleClick = () => {
      if (isReadonly) return;
      changeGroup({
        ...group,
        combinator: isAnd ? GroupCombinator.Or : GroupCombinator.And,
      });
    };

    // Use consistent chip-style for both layouts
    return (
      <span
        key={`c-${index}`}
        className={`text-xs font-medium px-1.5 py-0.5 rounded select-none ${isReadonly ? "" : "cursor-pointer transition-opacity hover:opacity-80"}`}
        style={{
          backgroundColor: isAnd ? "hsl(var(--heroui-primary) / 0.2)" : "hsl(var(--heroui-warning) / 0.2)",
          color: isAnd ? "hsl(var(--heroui-primary))" : "hsl(var(--heroui-warning))",
        }}
        onClick={handleClick}
      >
        {combinatorText}
      </span>
    );
  };

  const conditionElements: any[] = (filters || [])
    .map((f, i) => {
      const isNewFilter = newFilterIndex === i;
      const filterElement = (
        <Filter
          key={`f-${i}`}
          filter={f}
          filterDisplayMode={filterDisplayMode}
          layout={filterLayout}
          isReadonly={isReadonly}
          autoTriggerPropertySelector={isNewFilter}
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
          onCancelNewFilter={isNewFilter ? () => {
            setInternalNewFilterIndex(null);
            onExternalNewFilterConsumed?.();
            changeGroup({
              ...groupRef.current,
              filters: (groupRef.current.filters || []).filter((_, idx) => idx !== i),
            });
          } : undefined}
        />
      );
      return { element: filterElement, index: i };
    })
    .concat(
      (groups || []).map((g, i) => {
        const groupElement = (
          <FilterGroup
            key={`g-${i}`}
            group={g}
            filterDisplayMode={filterDisplayMode}
            filterLayout={filterLayout}
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

  // Final elements to render based on layout and mode
  const renderedElements = (() => {
    if (isVerticalLayout) {
      if (isSimpleMode) {
        // Vertical + simple: just stack elements vertically without combinators
        return conditionElements.map((item) => item.element);
      }
      // Vertical + advanced: wrap each filter with combinator prefix in a row
      return conditionElements.map((item, i) => {
        const isNotFirst = i > 0;
        return (
          <div key={`row-${item.index}`} className="flex items-center gap-1 w-full">
            {isNotFirst && renderCombinator(item.index)}
            <div className="flex-1">{item.element}</div>
          </div>
        );
      });
    }
    // Horizontal layout: interleave combinators between elements
    return conditionElements.reduce((acc: React.ReactNode[], item, i) => {
      acc.push(item.element);
      if (i < conditionElements.length - 1 && !isSimpleMode) {
        acc.push(renderCombinator(item.index));
      }
      return acc;
    }, []);
  })();

  // Group actions menu (delete/disable) - only for non-root groups
  const renderGroupActionsMenu = () => {
    if (isRoot || isReadonly) return null;

    return (
      <Popover
        placement="bottom-start"
        isOpen={actionsPopoverOpen}
        onOpenChange={setActionsPopoverOpen}
        trigger={
          <Button
            isIconOnly
            size="sm"
            variant="light"
            className="min-w-6 w-6 h-6"
          >
            <MoreOutlined className="text-base" />
          </Button>
        }
      >
        <div className="flex flex-col gap-1 p-1">
          <Button
            color="warning"
            size="sm"
            variant="light"
            className="justify-start"
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
                {t<string>("Enable group")}
              </>
            ) : (
              <>
                <MdOutlineFilterAltOff className="text-lg" />
                {t<string>("Disable group")}
              </>
            )}
          </Button>
          <Button
            color="danger"
            size="sm"
            variant="light"
            className="justify-start"
            onPress={() => {
              setActionsPopoverOpen(false);
              onRemove?.();
            }}
          >
            <DeleteOutlined className="text-base" />
            {t<string>("Delete group")}
          </Button>
        </div>
      </Popover>
    );
  };

  const renderGroup = () => {
    log("render group");

    // For vertical layout, wrap actions menu with first element on same line
    const renderElementsWithActionsMenu = () => {
      const actionsMenu = renderGroupActionsMenu();

      if (!actionsMenu || !isVerticalLayout) {
        // Horizontal mode or root group: render actions menu and elements separately
        return (
          <>
            {actionsMenu}
            {renderedElements}
          </>
        );
      }

      // Vertical mode with actions menu: put actions menu on same line as first element
      if (renderedElements.length === 0) {
        return actionsMenu;
      }

      const [firstElement, ...restElements] = renderedElements;
      return (
        <>
          <div className="flex items-center gap-1 w-full">
            {actionsMenu}
            <div className="flex-1">{firstElement}</div>
          </div>
          {restElements}
        </>
      );
    };

    return (
      <div
        className={`flex ${isVerticalLayout ? "flex-col" : "items-center flex-wrap"} gap-[5px] rounded text-sm relative ${isRoot ? "" : "border border-default-200 bg-[var(--bakaui-overlap-background)] px-1 py-1"}`}
      >
        {group.disabled && (
          <div
            className={
              "absolute top-0 left-0 w-full h-full flex items-center justify-center z-20 group/group-disable-cover rounded cursor-not-allowed"
            }
          >
            <MdOutlineFilterAltOff className={"text-lg text-warning"} />
          </div>
        )}
        {renderElementsWithActionsMenu()}
        {/* Hide add filter button in Simple mode - use FilterPortal instead */}
        {!isSimpleMode && !isReadonly && !(isRoot && !group && (!filters || filters.length == 0)) && (
          <Popover
            showArrow
            placement={"bottom"}
            isOpen={popoverOpen}
            onOpenChange={setPopoverOpen}
            trigger={
              <Button isIconOnly size={"sm"}>
                <TbFilterPlus className={"text-lg"} />
              </Button>
            }
          >
            <FilterAddPopoverContent
              showTags={false}
              showRecentFilters
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
              onSelectFilters={(filters) => {
                changeGroup({
                  ...groupRef.current,
                  filters: [...(groupRef.current.filters || []), ...filters],
                });
              }}
              onClose={() => setPopoverOpen(false)}
            />
          </Popover>
        )}
      </div>
    );
  };

  return renderGroup();
};

export default FilterGroup;
