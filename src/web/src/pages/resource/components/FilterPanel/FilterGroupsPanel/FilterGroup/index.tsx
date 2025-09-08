"use client";

"use strict";

import type { ResourceSearchFilterGroup } from "../models";

import { useTranslation } from "react-i18next";
import React, { useCallback, useEffect, useRef } from "react";
import { useUpdate, useUpdateEffect } from "react-use";
import {
  AppstoreOutlined,
  DeleteOutlined,
  FilterOutlined,
} from "@ant-design/icons";
import { TbFilterPlus } from "react-icons/tb";
import { MdOutlineFilterAlt, MdOutlineFilterAltOff } from "react-icons/md";

import { GroupCombinator } from "../models";

import styles from "./index.module.scss";
import Filter from "./Filter";

import { Button, Popover, Tooltip } from "@/components/bakaui";
import { buildLogger } from "@/components/utils";
import RecentFilters from "@/pages/resource/components/FilterPanel/RecentFilters";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import FilterModal from "../../FilterModal";

type Props = {
  group: ResourceSearchFilterGroup;
  onRemove?: () => void;
  onChange?: (group: ResourceSearchFilterGroup) => void;
  isRoot?: boolean;
};

const log = buildLogger("FilterGroup");

const FilterGroup = ({
  group: propsGroup,
  onRemove,
  onChange,
  isRoot = false,
}: Props) => {
  const { t } = useTranslation();
  const forceUpdate = useUpdate();
  const { createPortal } = useBakabaseContext();

  const [group, setGroup] =
    React.useState<ResourceSearchFilterGroup>(propsGroup);
  const groupRef = useRef(group);

  useEffect(() => {}, []);

  const changeGroup = useCallback(
    (newGroup: ResourceSearchFilterGroup) => {
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
    .map((f, i) => (
      <Filter
        key={`f-${i}`}
        filter={f}
        onChange={(tf) => {
          changeGroup({
            ...group,
            filters: (group.filters || []).map((fil) => (fil === f ? tf : fil)),
          });
        }}
        onRemove={() => {
          changeGroup({
            ...group,
            filters: (group.filters || []).filter((fil) => fil !== f),
          });
        }}
      />
    ))
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

  // log('tags ', tags?.map(t => t.toString()));

  const renderGroup = () => {
    log("render group");

    return (
      <div
        className={`${styles.filterGroup} p-1 ${isRoot ? styles.root : ""} ${styles.removable} relative`}
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
            style={{ zIndex: 10 }}
            trigger={
              <Button isIconOnly size={"sm"}>
                <TbFilterPlus className={"text-lg"} />
              </Button>
            }
          >
            <div className={"flex flex-col gap-2"}>
              <div className={"flex items-center gap-2"}>
                <Button
                  size={"sm"}
                  onPress={() => {
                    createPortal(
                      FilterModal, {
                        isNew: true,
                        filter: { disabled: false },
                        onSubmit: (filter) => {
                          changeGroup({
                            ...groupRef.current,
                            filters: [...(groupRef.current.filters || []), filter],
                          });
                        }
                      }
                    )
                  }}
                >
                  <FilterOutlined className={"text-base"} />
                  {t<string>("Filter")}
                </Button>
                <Button
                  size={"sm"}
                  onPress={() => {
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
                >
                  <AppstoreOutlined className={"text-base"} />
                  {t<string>("Filter group")}
                </Button>
              </div>
              <div className={"flex flex-col gap-1"}>
                <div>{t("Recent filters")}</div>
                <RecentFilters
                  onSelectFilter={(filter) => {
                    changeGroup({
                      ...groupRef.current,
                      filters: [...(groupRef.current.filters || []), filter],
                    });
                  }}
                />
              </div>
            </div>
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
