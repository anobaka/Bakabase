"use client";

import type { SearchForm } from "@/pages/resource/models";
import type { SavedSearchRef } from "@/pages/resource/components/FilterPanel/SavedSearches";

import { useTranslation } from "react-i18next";
import React, { useEffect, useRef, useState } from "react";
import { useUpdateEffect } from "react-use";
import {
  OrderedListOutlined,
  QuestionCircleOutlined,
  SaveOutlined,
  SearchOutlined,
  SnippetsOutlined,
} from "@ant-design/icons";
import toast from "react-hot-toast";
import { AiOutlineSearch } from "react-icons/ai";

import styles from "./index.module.scss";
import FilterGroupsPanel from "./FilterGroupsPanel";
import OrderSelector from "./OrderSelector";
import FilterPortal from "./FilterPortal";

import BApi from "@/sdk/BApi";
import { useUiOptionsStore } from "@/models/options";
import { PlaylistCollection } from "@/components/Playlist";
import {
  Autocomplete,
  AutocompleteItem,
  Button,
  Checkbox,
  Chip,
  Input,
  Modal,
  Popover,
  Spinner,
  Tooltip,
} from "@/components/bakaui";
import { MdPlaylistPlay } from "react-icons/md";
import SavedSearches from "@/pages/resource/components/FilterPanel/SavedSearches";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import MiscellaneousOptions from "@/pages/resource/components/FilterPanel/MiscellaneousOptions";
import { ResourceTag } from "@/sdk/constants";
import HandleUnknownResources from "@/components/HandleUnknownResources";

interface IProps {
  selectedResourceIds?: number[];
  maxResourceColCount?: number;
  searchForm?: SearchForm;
  onSearch?: (form: Partial<SearchForm>) => Promise<any>;
  reloadResources: (ids: number[]) => any;
  multiSelection?: boolean;
  rearrangeResources?: () => any;
  onSelectAllChange: (selected: boolean, includeNotLoaded?: boolean) => any;
  resourceCount?: number;
  totalFilteredResourceCount?: number;
}

const MinResourceColCount = 3;
const DefaultResourceColCount = 6;
const DefaultMaxResourceColCount = 10;

const defaultSearchForm = (): SearchForm => ({
  page: 1,
  pageSize: 0,
});

export default ({
  maxResourceColCount = DefaultMaxResourceColCount,
  selectedResourceIds,
  onSearch,
  searchForm: propsSearchForm,
  multiSelection = false,
  rearrangeResources,
  onSelectAllChange,
  resourceCount,
  totalFilteredResourceCount,
}: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const uiOptions = useUiOptionsStore((state) => state.data);

  const [colCountsDataSource, setColCountsDataSource] = useState<
    { label: any; value: number }[]
  >([]);
  const colCount = uiOptions.resource?.colCount ?? DefaultResourceColCount;

  const [selectedAll, setSelectedAll] = useState(false);

  const [searchForm, setSearchForm] = useState<SearchForm>(
    propsSearchForm || defaultSearchForm(),
  );
  const [searching, setSearching] = useState(false);

  const [selectingAllFilteredResources, setSelectingAllFilteredResources] =
    useState(false);

  const savedSearchesRef = useRef<SavedSearchRef>(null);

  const [isLoadingKeywords, setIsLoadingKeywords] = React.useState(false);
  const [keywordCandidates, setKeywordCandidates] = React.useState<string[]>(
    [],
  );
  const debounceTimer = useRef<NodeJS.Timeout | null>(null);

  useUpdateEffect(() => {
    setSearchForm(propsSearchForm || defaultSearchForm());
  }, [propsSearchForm]);

  useEffect(() => {
    const ccds: { label: any; value: number }[] = [];

    for (let i = MinResourceColCount; i <= maxResourceColCount; i++) {
      ccds.push({
        label: i,
        value: i,
      });
    }
    setColCountsDataSource(ccds);
  }, [maxResourceColCount]);

  useUpdateEffect(() => {
    console.log("Search form changed", searchForm);
  }, [searchForm]);

  const search = async (patches: Partial<SearchForm>) => {
    if (onSearch) {
      setSearching(true);

      try {
        await onSearch(patches);
      } catch (e) {
        console.error(e);
      } finally {
        setSearching(false);
      }
    }
  };

  console.log("resource page filter panel rerender", searchForm);

  return (
    <div className={`${styles.filterPanel} flex flex-col gap-1`}>
      <div className={"flex items-center gap-4"}>
        <Autocomplete
          isClearable
          className={"w-1/4 min-w-[200px]"}
          inputValue={searchForm.keyword}
          isLoading={isLoadingKeywords}
          items={keywordCandidates.map((k) => ({ label: k, value: k }))}
          placeholder={t<string>("Search everything")}
          selectedKey={null}
          startContent={<SearchOutlined className={"text-xl"} />}
          onInputChange={(v) => {
            if (debounceTimer.current) {
              clearTimeout(debounceTimer.current);
            }
            if (v != undefined && v.length > 0) {
              setIsLoadingKeywords(true);
              debounceTimer.current = setTimeout(() => {
                BApi.resource
                  .getResourceSearchKeywordRecommendation({
                    keyword: v,
                  })
                  .then((ret) => {
                    setKeywordCandidates(ret.data ?? []);
                  })
                  .finally(() => {
                    setIsLoadingKeywords(false);
                  });
              }, 300);
            }
          }}
          onKeyDown={(e) => {
            if (e.key == "Enter") {
              search({
                ...searchForm,
                page: 1,
              });
            }
          }}
          onSelectionChange={(k) => {
            console.log("onSelectionChange", k);
            if (k) {
              const nf = {
                keyword: k as string,
                page: 1,
              };

              search(nf);
            }
          }}
          onValueChange={(v) => {
            setSearchForm({
              ...searchForm,
              keyword: v,
            });
          }}
        >
          {(item) => (
            <AutocompleteItem key={item.value} title={item.label}>
              {item.label}
            </AutocompleteItem>
          )}
        </Autocomplete>
        <FilterPortal
          searchForm={searchForm}
          onChange={() => {
            setSearchForm({
              ...searchForm,
            });
          }}
        />{" "}
        <SavedSearches
          ref={savedSearchesRef}
          onSelect={(nf) => {
            search(nf);
          }}
        />
      </div>
      {searchForm.group && (
        <FilterGroupsPanel
          group={searchForm.group}
          onChange={(v) => {
            setSearchForm({
              ...searchForm,
              group: v,
            });
          }}
        />
      )}
      {searchForm.tags && searchForm.tags.length > 0 && (
        <div className={"flex flex-wrap gap-1 mb-2"}>
          {searchForm.tags.map((tag, i) => {
            return (
              <Chip
                key={i}
                isCloseable
                size={"sm"}
                onClose={() => {
                  setSearchForm({
                    ...searchForm,
                    tags: searchForm.tags?.filter((t) => t != tag),
                  });
                }}
              >
                {t<string>(`ResourceTag.${ResourceTag[tag]}`)}
              </Chip>
            );
          })}
        </div>
      )}
      <div className={"flex items-center justify-between"}>
        <div className={"flex items-center gap-4"}>
          <Button
            color={"primary"}
            isLoading={searching}
            size={"sm"}
            onPress={async () => {
              await search({
                ...searchForm,
                page: 1,
              });
            }}
          >
            <AiOutlineSearch className={"text-base"} />
            {t<string>("Search")}
          </Button>
          <Tooltip
            content={t<string>("Save current search to quick search")}
            placement={"right"}
          >
            <Button
              isIconOnly
              size={"sm"}
              onPress={() => {
                let name = `${t<string>("Untitled search")}1`;

                createPortal(Modal, {
                  defaultVisible: true,
                  size: "lg",
                  title: t<string>("Save current search"),
                  children: (
                    <Input
                      isRequired
                      defaultValue={name}
                      label={t<string>("Name")}
                      placeholder={t<string>(
                        "Please set a name for current search",
                      )}
                      onValueChange={(v) => (name = v?.trim())}
                    />
                  ),
                  onOk: async () => {
                    if (name != undefined && name.length > 0) {
                      // @ts-ignore
                      await BApi.resource.saveNewResourceSearch({
                        search: searchForm,
                        name,
                      });
                      savedSearchesRef.current?.reload();
                    } else {
                      toast.error(t<string>("Name is required"));
                      throw new Error("Name is required");
                    }
                  },
                });
              }}
            >
              <SaveOutlined className={"text-base"} />
            </Button>
          </Tooltip>
        </div>
        <div className={"flex items-center gap-2"}>
          {multiSelection && (
            <Chip color={"success"} variant={"light"}>
              <SnippetsOutlined className={"text-base"} />
            </Chip>
          )}
          <Popover
            color={"success"}
            trigger={<QuestionCircleOutlined className={"text-base"} />}
          >
            <div className={"flex flex-col gap-1"}>
              <div>
                {t<string>("Hold down Ctrl to select multiple resources.")}
              </div>
              <div>
                {t<string>(
                  "You can perform more actions by right-clicking on the resource.",
                )}
              </div>
            </div>
          </Popover>
          <Tooltip
            content={
              <div className={"flex items-center gap-1"}>
                {t<string>("Resources loaded in current page")}
                {selectingAllFilteredResources ? (
                  <Spinner size={"sm"} />
                ) : (
                  totalFilteredResourceCount != resourceCount && (
                    <Button
                      color={"primary"}
                      size={"sm"}
                      variant={"light"}
                      onPress={async () => {
                        setSelectedAll(true);
                        setSelectingAllFilteredResources(true);
                        try {
                          const ret = onSelectAllChange(true, true);

                          if (!!ret && typeof ret.then === "function") {
                            await ret;
                          }
                        } finally {
                          setSelectingAllFilteredResources(false);
                        }
                      }}
                    >
                      {t<string>(
                        "Select all {{count}} filtered resources (including those not currently loaded).",
                        { count: totalFilteredResourceCount },
                      )}
                    </Button>
                  )
                )}
              </div>
            }
          >
            <Checkbox
              isSelected={
                selectedAll &&
                selectedResourceIds &&
                selectedResourceIds?.length > 0
              }
              size={"sm"}
              onValueChange={(isSelected) => {
                onSelectAllChange(isSelected);
                setSelectedAll(isSelected);
              }}
            >
              {selectedAll
                ? t<string>("{{count}} items selected", {
                  count: selectedResourceIds?.length,
                })
                : t<string>("Select all")}
            </Checkbox>
          </Tooltip>
          <HandleUnknownResources onHandled={() => search({})} />
          {totalFilteredResourceCount && totalFilteredResourceCount > 0 ? (
            <div className={"flex items-center gap-1"}>
              <Tooltip content={t<string>("Loaded resources")}>
                <Chip size='sm' variant="light" color={"success"}>{resourceCount}</Chip>
              </Tooltip>
              /
              <Tooltip content={t<string>("All filtered resources")}>
                <Chip size='sm' variant="light" color={"secondary"}>
                  {totalFilteredResourceCount}
                </Chip>
              </Tooltip>
            </div>
          ) : null}
          <OrderSelector
            className={"mr-2"}
            value={searchForm.orders}
            onChange={(orders) => {
              const nf = {
                ...searchForm,
                orders,
              };

              setSearchForm(nf);
              search(nf);
            }}
          />
          <Popover
            trigger={
              <Button
                color={"default"}
                size={"sm"}
                startContent={
                  <MdPlaylistPlay className={"text-xl"} />
                }
              >
                {t<string>("Playlist")}
              </Button>
            }
          >
            <PlaylistCollection className={"resource-page"} />
          </Popover>
          <Popover
            placement={"bottom-end"}
            trigger={
              <Button
                color={"default"}
                size={"sm"}
                startContent={<OrderedListOutlined />}
              >
                {t<string>("Column count")}
                &nbsp;
                {colCount}
              </Button>
            }
          >
            <div className={"grid grid-cols-4 gap-1 p-1 rounded"}>
              {colCountsDataSource.map((cc, i) => {
                return (
                  <Button
                    key={i}
                    className={"min-w-0 pl-2 pr-2"}
                    color={"default"}
                    size={"sm"}
                    onClick={async () => {
                      const patches = {
                        resource: {
                          ...(uiOptions?.resource || {}),
                          colCount: cc.value,
                        },
                      };

                      await BApi.options.patchUiOptions(patches);
                    }}
                  >
                    {cc.label}
                  </Button>
                );
              })}
            </div>
          </Popover>
          <MiscellaneousOptions rearrangeResources={rearrangeResources} />
        </div>
      </div>
    </div>
  );
};
