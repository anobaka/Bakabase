"use client";

"use strict";

import type { SearchFilterGroup } from "../../models";

import { useTranslation } from "react-i18next";
import React, { useCallback, useRef, useState } from "react";
import { useUpdateEffect } from "react-use";
import { DeleteOutlined } from "@ant-design/icons";
import { TbFilterPlus } from "react-icons/tb";
import { MdOutlineFilterAlt, MdOutlineFilterAltOff } from "react-icons/md";

import { GroupCombinator } from "../../models";
import Filter from "../Filter";
import FilterAddPopoverContent from "../FilterAddPopoverContent";

import { Button, Popover, Tooltip } from "@/components/bakaui";
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
};

const log = buildLogger("FilterGroup");

const FilterGroup = ({
  group: propsGroup,
  onRemove,
  onChange,
  isRoot = false,
  externalNewFilterIndex,
  onExternalNewFilterConsumed,
}: Props) => {
  const { t } = useTranslation();

  const [group, setGroup] = React.useState<SearchFilterGroup>(propsGroup);
  const groupRef = useRef(group);
  const [popoverOpen, setPopoverOpen] = useState(false);
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

  const conditionElements: any[] = (filters || [])
    .map((f, i) => {
      const isNewFilter = newFilterIndex === i;
      return (
        <Filter
          key={`f-${i}`}
          filter={f}
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
    })
    .concat(
      (groups || []).map((g, i) => (
        <FilterGroup
          key={`g-${i}`}
          group={g}
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
      )),
    );

  const allElements = conditionElements.reduce((acc, el, i) => {
    acc.push(el);
    if (i < conditionElements.length - 1) {
      acc.push(
        <Button
          key={`c-${i}`}
          className={"min-w-fit pl-2 pr-2"}
          color={"default"}
          size={"sm"}
          variant={"light"}
          onClick={() => {
            changeGroup({
              ...group,
              combinator:
                GroupCombinator.And == group.combinator
                  ? GroupCombinator.Or
                  : GroupCombinator.And,
            });
          }}
        >
          {t<string>(`Combinator.${GroupCombinator[combinator]}`)}
        </Button>,
      );
    }

    return acc;
  }, []);

  const renderGroup = () => {
    log("render group");

    return (
      <div
        className={`flex items-center flex-wrap gap-[5px] rounded text-sm px-1 relative ${isRoot ? "" : "bg-[var(--bakaui-overlap-background)]"}`}
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
        {allElements}
        {!(isRoot && !group && (!filters || filters.length == 0)) && (
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
              showQuickFilters
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
              onSelectFilter={(filter) => {
                changeGroup({
                  ...groupRef.current,
                  filters: [...(groupRef.current.filters || []), filter],
                });
              }}
              onClose={() => setPopoverOpen(false)}
            />
          </Popover>
        )}
      </div>
    );
  };

  return isRoot ? (
    renderGroup()
  ) : (
    <Tooltip
      content={
        <div className={"flex items-center gap-1"}>
          <Button
            color={"warning"}
            size={"sm"}
            variant={"light"}
            onPress={() => {
              changeGroup({
                ...group,
                disabled: !group.disabled,
              });
            }}
          >
            {group.disabled ? (
              <>
                <MdOutlineFilterAlt className={"text-lg"} />
                {t<string>("Enable group")}
              </>
            ) : (
              <>
                <MdOutlineFilterAltOff className={"text-lg"} />
                {t<string>("Disable group")}
              </>
            )}
          </Button>
          <Button
            color={"danger"}
            size={"sm"}
            variant={"light"}
            onPress={onRemove}
          >
            <DeleteOutlined className={"text-base"} />
            {t<string>("Delete group")}
          </Button>
        </div>
      }
    >
      {renderGroup()}
    </Tooltip>
  );
};

export default FilterGroup;
