"use client";

import type { SearchForm } from "@/pages/resource/models";
import type {
  ResourceSearchFilter,
  ResourceSearchFilterGroup,
} from "@/pages/resource/components/FilterPanel/FilterGroupsPanel/models";

import { CheckOutlined, DeleteOutlined, SaveOutlined } from "@ant-design/icons";
import React, {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useState,
} from "react";
import { useTranslation } from "react-i18next";
import { useUpdate } from "react-use";

import {
  ResourceSearchSortableProperty,
  ResourceTag,
  SearchCombinator,
  SearchOperation,
} from "@/sdk/constants";
import {
  Button,
  Chip,
  Divider,
  Input,
  Popover,
  Tooltip,
} from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import BApi from "@/sdk/BApi";

type SavedSearch = {
  name: string;
  search: SearchForm;
};

type Props = {
  onSelect?: (search: SearchForm) => any;
};

export type SavedSearchRef = {
  reload: () => any;
};

const SavedSearches = forwardRef<SavedSearchRef, Props>((props, ref) => {
  const { onSelect } = props;

  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const [savedSearches, setSavedSearches] = useState<SavedSearch[]>([]);

  useEffect(() => {
    reload();
  }, []);

  const reload = async () => {
    const searches = await BApi.resource.getSavedSearches();

    setSavedSearches(searches.data || []);
  };

  useImperativeHandle(ref, () => ({
    reload,
  }));

  const renderFilterValue = (filter: ResourceSearchFilter) => {
    if (
      filter.operation == SearchOperation.IsNotNull ||
      filter.operation == SearchOperation.IsNull ||
      !filter.valueProperty
    ) {
      return null;
    }

    return (
      <Chip
        variant={'light'}
        // color={'primary'}
        size={'sm'}
      >
        {filter.dbValue == undefined ? (
          t<string>("Not set")
        ) : (
          <PropertyValueRenderer
            bizValue={filter.bizValue}
            dbValue={filter.dbValue}
            property={filter.valueProperty}
            variant={"light"}
          />
        )}
      </Chip>
    );
  };

  const renderFilter = (filter: ResourceSearchFilter, isOutermost: boolean) => {
    return (
      <div className={`flex items-center gap-1 ${isOutermost ? "" : "p-1"} `}>
        <Chip color={"primary"} size={"sm"} variant={"light"}>
          {filter.property?.name ?? t<string>("Not set")}
        </Chip>
        <Chip color={"secondary"} size={"sm"} variant={"light"}>
          {filter.operation == undefined
            ? t<string>("Not set")
            : t<string>(`SearchOperation.${SearchOperation[filter.operation]}`)}
        </Chip>
        {renderFilterValue(filter)}
      </div>
    );
  };

  const renderGroup = (
    group: ResourceSearchFilterGroup,
    isOutermost: boolean,
  ) => {
    const groups = group.groups ?? [];
    const filters = group.filters ?? [];

    if (groups.length + filters.length == 0) {
      return <div />;
    }

    return (
      <div
        className={`flex items-center gap-1 flex-wrap rounded bg-[var(--bakaui-overlap-background)] p-1 ${isOutermost ? "" : "p-1"} `}
      >
        {filters.map((filter, i) => {
          return (
            <>
              {renderFilter(filter, isOutermost)}
              {(i != group.filters!.length - 1 ||
                (group.groups && group.groups.length > 0)) && (
                <Chip color={"success"} size={"sm"} variant={"light"}>
                  {t<string>(
                    `Combinator.${SearchCombinator[group.combinator]}`,
                  )}
                </Chip>
              )}
            </>
          );
        })}
        {groups.map((subGroup, i) => {
          return (
            <>
              {renderGroup(subGroup, false)}
              {i != group.groups!.length - 1 && (
                <Chip color={"success"} size={"sm"} variant={"light"}>
                  {t<string>(
                    `Combinator.${SearchCombinator[group.combinator]}`,
                  )}
                </Chip>
              )}
            </>
          );
        })}
      </div>
    );
  };

  console.log("saved search rerender");

  return (
    <div className={"flex items-center flex-wrap gap-1"}>
      {savedSearches.map((savedSearch, idx) => {
        const { search, name } = savedSearch;

        return (
          <Tooltip
            classNames={{
              content: "max-w-[800px] py-2",
            }}
            content={
              <div className={"flex flex-col gap-2"}>
                <div className={"flex items-center gap-1"}>
                  <Button
                    isIconOnly
                    color={"primary"}
                    isDisabled={name == undefined || name.length == 0}
                    size={"sm"}
                    variant={"light"}
                    onClick={() => {
                      BApi.resource.putSavedSearchName(idx, name);
                    }}
                  >
                    <SaveOutlined className={"text-large"} />
                  </Button>
                  <Input
                    size={"sm"}
                    value={name}
                    onValueChange={(n) => {
                      savedSearch.name = n;
                      forceUpdate();
                    }}
                  />
                  <Popover
                    trigger={
                      <Button
                        isIconOnly
                        color={"danger"}
                        size={"sm"}
                        variant={"light"}
                      >
                        <DeleteOutlined className={"text-large"} />
                      </Button>
                    }
                  >
                    <div className={"flex items-center gap-2"}>
                      <Button
                        isIconOnly
                        color={"danger"}
                        size={"sm"}
                        variant={"light"}
                        onClick={() => {
                          BApi.resource.deleteSavedSearch(idx).then((r) => {
                            if (!r.code) {
                              reload();
                            }
                          });
                        }}
                      >
                        <CheckOutlined className={"text-large"} />
                      </Button>
                    </div>
                  </Popover>
                </div>
                <Divider orientation={"horizontal"} />
                <div
                  className={"grid gap-1 items-center"}
                  style={{ gridTemplateColumns: "auto minmax(0, 1fr)" }}
                >
                  <div className={"text-right"}>
                    <Chip radius={"sm"} size={"sm"} variant={"bordered"}>
                      {t<string>("Keyword")}
                    </Chip>
                  </div>
                  <div>{search.keyword}</div>
                  <div className={"text-right"}>
                    <Chip radius={"sm"} size={"sm"} variant={"bordered"}>
                      {t<string>("Filters")}
                    </Chip>
                  </div>
                  <div>{search.group && renderGroup(search.group, true)}</div>
                  <div className={"text-right"}>
                    <Chip radius={"sm"} size={"sm"} variant={"bordered"}>
                      {t<string>("Order")}
                    </Chip>
                  </div>
                  <div className={"flex items-center gap-1"}>
                    {search.orders?.map((o) => {
                      return (
                        <Chip radius={"sm"} size={"sm"}>
                          {t<string>(
                            ResourceSearchSortableProperty[o.property],
                          )}
                          {t<string>(o.asc ? "Asc" : "Desc")}
                        </Chip>
                      );
                    })}
                  </div>
                  <div className={"text-right"}>
                    <Chip radius={"sm"} size={"sm"} variant={"bordered"}>
                      {t<string>("Page")}
                    </Chip>
                  </div>
                  <div>{search.page}</div>
                  <div className={"text-right"}>
                    <Chip radius={"sm"} size={"sm"} variant={"bordered"}>
                      {t<string>("Special filters")}
                    </Chip>
                  </div>
                  <div className={"flex flex-wrap gap-1"}>
                    {search.tags?.map((x) => {
                      return (
                        <Chip
                          size={'sm'}
                          radius={"sm"}
                          // variant={'bordered'}
                        >
                          {t<string>(`ResourceTag.${ResourceTag[x]}`)}
                        </Chip>
                      );
                    })}
                  </div>
                </div>
              </div>
            }
          >
            <Button
              size={"sm"}
              variant={"ghost"}
              onClick={() => {
                onSelect?.(search);
              }}
            >
              {name}
            </Button>
          </Tooltip>
        );
      })}
    </div>
  );
});

export default SavedSearches;
