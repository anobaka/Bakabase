"use client";

import type { BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions } from "@/sdk/Api";

import {
  AppstoreAddOutlined,
  DashboardOutlined,
  EyeOutlined,
  FullscreenOutlined,
  PlayCircleOutlined,
  QuestionCircleOutlined,
  ZoomInOutlined,
} from "@ant-design/icons";
import React from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { AiOutlineFieldNumber } from "react-icons/ai";
import { BiCarousel } from "react-icons/bi";

import {
  Button,
  Dropdown,
  DropdownItem,
  DropdownMenu,
  DropdownTrigger,
  Tooltip,
} from "@/components/bakaui";
import {
  CoverFit,
  ResourceDisplayContent,
  resourceDisplayContents,
} from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useUiOptionsStore } from "@/stores/options";
import { buildLogger } from "@/components/utils";

type Props = {
  rearrangeResources?: () => any;
};

const log = buildLogger("MiscellaneousOptions");

type Item = {
  key: ListBoxItemKey;
  label: string;
  Icon: React.FC<{ className?: string }>;
  tip?: any;
};

type ListBoxItemKey =
  | "FillCover"
  | "ShowLargerCoverOnHover"
  | "PreviewOnHover"
  | "UseCache"
  | "CoverCarousel"
  | "DisplayResourceId";
const MiscellaneousOptions = ({ rearrangeResources }: Props) => {
  const { t } = useTranslation();
  const uiOptions = useUiOptionsStore((state) => state.data);
  const options = uiOptions.resource;

  const currentResourceDisplayContents =
    uiOptions.resource?.displayContents ?? ResourceDisplayContent.All;
  const selectableResourceDisplayContents = resourceDisplayContents
    .filter((d) => d.value != ResourceDisplayContent.All)
    .map((x) => ({
      label: t<string>("Show") + t<string>(x.label),
      value: x.value.toString(),
    }));

  const items: Item[] = [
    ...selectableResourceDisplayContents.map((d) => ({
      key: `DisplayContent-${d.value.toString()}` as ListBoxItemKey,
      label: d.label,
      Icon: EyeOutlined,
    })),
    {
      key: "FillCover",
      label: t<string>("Fill cover"),
      Icon: FullscreenOutlined,
    },
    {
      key: "ShowLargerCoverOnHover",
      label: t<string>("Show larger cover on mouse hover"),
      Icon: ZoomInOutlined,
    },
    {
      key: "PreviewOnHover",
      label: t<string>("Preview files of a resource on mouse hover"),
      Icon: PlayCircleOutlined,
    },
    {
      key: "UseCache",
      label: t<string>("Use cache"),
      Icon: DashboardOutlined,
      tip: (
        <div className={"max-w-[400px]"}>
          {t<string>(
            "Enabling cache can improve loading speed, but your covers and playable files will not be updated in time unless you clear or disable cache manually.",
          )}
          <Button
            size={"sm"}
            variant={"flat"}
            // color={'secondary'}
            onClick={() => {
              navigate("/cache");
            }}
          >
            {t<string>("Manage cache")}
          </Button>
        </div>
      ),
    },
    {
      key: "CoverCarousel",
      label: t<string>("Cover carousel"),
      Icon: BiCarousel,
    },
    {
      key: "DisplayResourceId",
      label: t<string>("Display resource ID"),
      Icon: AiOutlineFieldNumber,
    },
  ];

  const navigate = useNavigate();

  const buildSelectedKeys = () => {
    const keys: ListBoxItemKey[] = [];

    if (options?.coverFit === CoverFit.Cover) {
      keys.push("FillCover");
    }
    if (!options?.disableCache) {
      keys.push("UseCache");
    }
    if (options?.showBiggerCoverWhileHover) {
      keys.push("ShowLargerCoverOnHover");
    }
    if (!options?.disableMediaPreviewer) {
      keys.push("PreviewOnHover");
    }
    for (const d of selectableResourceDisplayContents) {
      if (currentResourceDisplayContents & d.value) {
        keys.push(`DisplayContent-${d.value}` as ListBoxItemKey);
      }
    }
    if (!options?.disableCoverCarousel) {
      keys.push("CoverCarousel");
    }
    if (options?.displayResourceId) {
      keys.push("DisplayResourceId");
    }

    return keys;
  };

  const selectedKeys = buildSelectedKeys();

  return (
    <Dropdown>
      <DropdownTrigger>
        <Button isIconOnly size={"sm"} variant={"light"}>
          <AppstoreAddOutlined className={"text-xl"} />
        </Button>
      </DropdownTrigger>
      <DropdownMenu
        aria-label="Multiple selection example"
        closeOnSelect={false}
        color={'primary'}
        variant="flat"
        onSelectionChange={keys => {
          if (keys instanceof Set) {
            const stringKeys: ListBoxItemKey[] = Array.from(keys).map(k => k.toString() as ListBoxItemKey);
            let dc: ResourceDisplayContent = 0;
            for (const key of stringKeys) {
              if (key.startsWith('DisplayContent-')) {
                dc |= parseInt(key.replace('DisplayContent-', ''), 10);
              }
            }

            const newOptions: BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions = {
              ...options,
              coverFit: stringKeys.includes('FillCover') ? CoverFit.Cover : CoverFit.Contain,
              disableCache: !stringKeys.includes('UseCache'),
              showBiggerCoverWhileHover: stringKeys.includes('ShowLargerCoverOnHover'),
              disableMediaPreviewer: !stringKeys.includes('PreviewOnHover'),
              displayContents: dc,
              disableCoverCarousel: !stringKeys.includes('CoverCarousel'),
              displayResourceId: stringKeys.includes('DisplayResourceId'),
            };

            log(keys, newOptions);
            BApi.options.patchUiOptions({
              resource: newOptions,
            }).then(r => {
              if ((currentResourceDisplayContents & ResourceDisplayContent.Tags) != (dc & ResourceDisplayContent.Tags)) {
                rearrangeResources?.();
              }
            });
          }
        }}
        selectionMode="multiple"
        // selectedKeys={selectableResourceDisplayContents.filter(c => currentResourceDisplayContents & c.value).map(c => c.value.toString())}
        selectedKeys={selectedKeys}
      >
        {items.map((item) => {
          return (
            <DropdownItem
              key={item.key}
              className={selectedKeys.includes(item.key) ? "text-primary" : ""}
              startContent={<item.Icon className={"text-base"} />}
            >
              {item.tip ? (
                <div className={"flex items-center gap-1"}>
                  {item.label}
                  <Tooltip
                    color={"secondary"}
                    content={item.tip}
                    placement={"left"}
                  >
                    <QuestionCircleOutlined className={"text-base"} />
                  </Tooltip>
                </div>
              ) : (
                item.label
              )}
            </DropdownItem>
          );
        })}
      </DropdownMenu>
    </Dropdown>
  );
};

MiscellaneousOptions.displayName = "MiscellaneousOptions";

export default MiscellaneousOptions;
