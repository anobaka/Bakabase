"use client";

import type { ResourceTag } from "@/sdk/constants";
import type { SearchForm } from "@/pages/resource/models.ts";

import { AppstoreOutlined, FilterOutlined } from "@ant-design/icons";
import { TbFilterPlus } from "react-icons/tb";
import { useTranslation } from "react-i18next";

import QuickFilter from "./FilterGroupsPanel/QuickFilter";
import { addFilter, addFilterGroup } from "./utils";

import {
  Button,
  Checkbox,
  CheckboxGroup,
  Divider,
  Popover,
} from "@/components/bakaui";
import { resourceTags } from "@/sdk/constants";
import RecentFilters from "@/pages/resource/components/FilterPanel/RecentFilters";

interface FilterPortalProps {
  searchForm: SearchForm;
  onChange: () => void;
}

export default function FilterPortal({
  searchForm,
  onChange,
}: FilterPortalProps) {
  const { t } = useTranslation();

  return (
    <Popover
      showArrow
      placement={"bottom"}
      style={{ zIndex: 10 }}
      trigger={
        <Button isIconOnly>
          <TbFilterPlus className={"text-lg"} />
        </Button>
      }
    >
      <div
        className={"grid items-center gap-2 my-3 mx-1"}
        style={{ gridTemplateColumns: "auto auto" }}
      >
        <QuickFilter
          onAdded={(newFilter) => {
            addFilter(searchForm, newFilter);
            onChange();
          }}
        />
        <div />
        <Divider orientation={"horizontal"} />
        <div>{t<string>("Advance filter")}</div>
        <div className={"flex items-center gap-2"}>
          <Button
            size={"sm"}
            onPress={() => {
              addFilter(searchForm);
              onChange();
            }}
          >
            <FilterOutlined className={"text-base"} />
            {t<string>("Filter")}
          </Button>
          <Button
            size={"sm"}
            onPress={() => {
              addFilterGroup(searchForm);
              onChange();
            }}
          >
            <AppstoreOutlined className={"text-base"} />
            {t<string>("Filter group")}
          </Button>
        </div>
        <div />
        <Divider orientation={"horizontal"} />
        <div>{t<string>("Special filters")}</div>
        <div>
          <CheckboxGroup
            size={"sm"}
            value={searchForm.tags?.map((t) => t.toString())}
            onChange={(ts) => {
              searchForm.tags = ts.map((t) => parseInt(t, 10) as ResourceTag);
              onChange();
            }}
          >
            {resourceTags.map((rt) => {
              return (
                <Checkbox key={rt.value} value={rt.value.toString()}>
                  {t<string>(`ResourceTag.${rt.label}`)}
                </Checkbox>
              );
            })}
          </CheckboxGroup>
        </div>
        <div />
        <Divider />
        <div>{t<string>("Recent filters")}</div>
        <div>
          <RecentFilters
            onSelectFilter={(newFilter) => {
              addFilter(searchForm, newFilter);
              onChange();
            }}
          />
        </div>
      </div>
    </Popover>
  );
}
