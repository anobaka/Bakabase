"use client";

import type { BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions } from "@/sdk/Api";
import type { PropertyMap } from "@/components/types";

import {
  AppstoreAddOutlined,
  CloseOutlined,
  DatabaseOutlined,
  FireOutlined,
  FolderOpenOutlined,
  FullscreenOutlined,
  PlayCircleOutlined,
  ProductOutlined,
  PushpinOutlined,
  VideoCameraAddOutlined,
  ZoomInOutlined,
} from "@ant-design/icons";
import { AiOutlinePicture } from "react-icons/ai";
import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { AiOutlineFieldNumber, AiOutlineSetting } from "react-icons/ai";
import { BiCarousel } from "react-icons/bi";
import _ from "lodash";
import { ImEmbed } from "react-icons/im";
import { Slider } from "@heroui/react";

import { Button, Checkbox, Chip, Divider, Modal, ButtonGroup, Input, Tab, Tabs } from "@/components/bakaui";
import { CoverFit, PropertyPool } from "@/sdk/constants";
import { useFilterOptionsThreshold, DEFAULT_FILTER_OPTIONS_THRESHOLD } from "@/hooks/useFilterOptionsThreshold";
import BApi from "@/sdk/BApi";
import { useUiOptionsStore, useUiStyleOptionsStore } from "@/stores/options";
import { buildLogger } from "@/components/utils";
import PropertySelector from "@/components/PropertySelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { PropertyLabel } from "@/components/Property";
import BriefProperty from "@/components/Chips/Property/BriefProperty";
import { cssVariableGroups } from "@/cons/uiStyleVariables";

type Props = {
  rearrangeResources?: () => any;
};

const log = buildLogger("MiscellaneousOptions");

const MinResourceColCount = 3;
const DefaultResourceColCount = 6;
const MaxResourceColCount = 12;

const MiscellaneousOptions = ({ rearrangeResources }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const uiOptionsStore = useUiOptionsStore();
  const uiStyleOptionsStore = useUiStyleOptionsStore();
  const cssOverwrites = uiStyleOptionsStore.data?.cssVariableOverwrites ?? {};
  const resourceUiOptions = uiOptionsStore.data?.resource;
  const currentColCount = resourceUiOptions?.colCount ?? DefaultResourceColCount;
  const [propertyMap, setPropertyMap] = useState<PropertyMap>({});
  const [filterOptionsThreshold, setFilterOptionsThreshold] = useFilterOptionsThreshold();
  // Local slider values for immediate visual feedback during drag
  const [localSliderValues, setLocalSliderValues] = useState<Record<string, number>>({});

  const navigate = useNavigate();

  const loadProperties = async () => {
    const psr = (await BApi.property.getPropertiesByPool(PropertyPool.All)).data || [];
    const ps = _.mapValues(
      _.groupBy(psr, (x) => x.pool),
      (v) => _.keyBy(v, (x) => x.id),
    );

    setPropertyMap(ps);
  };

  useEffect(() => {
    loadProperties();
  }, []);

  const [visible, setVisible] = useState(false);

  const patchOptions = async (
    options: Partial<BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions>,
  ) => {
    await uiOptionsStore.patch({ resource: { ...resourceUiOptions, ...options } });
  };

  const renderGeneralTab = () => (
    <div className={"flex flex-col gap-1"}>
      <div className={"flex items-center gap-1"}>
        <div className={"text-sm"}>{t<string>("resource.display.columnCount")}</div>
        <div className={"flex flex-wrap gap-1"}>
          <ButtonGroup>
            {Array.from(
              { length: MaxResourceColCount - MinResourceColCount + 1 },
              (_, idx) => idx + MinResourceColCount,
            ).map((num) => (
              <Button
                key={num}
                color={currentColCount === num ? "primary" : "default"}
                size={"sm"}
                onPress={() => patchOptions({ colCount: num })}
              >
                {num}
              </Button>
            ))}
          </ButtonGroup>
        </div>
      </div>
      <div>
        <Checkbox
          size="sm"
          isSelected={resourceUiOptions?.coverFit === CoverFit.Cover}
          onValueChange={(checked) =>
            patchOptions({ coverFit: checked ? CoverFit.Cover : CoverFit.Contain })
          }
        >
          <div className={"flex items-center gap-1"}>
            <FullscreenOutlined className={"text-base"} />
            {t<string>("resource.display.fillCover")}
          </div>
        </Checkbox>
      </div>

      <div>
        <Checkbox
          size="sm"
          isSelected={!!resourceUiOptions?.showBiggerCoverWhileHover}
          onValueChange={(checked) => patchOptions({ showBiggerCoverWhileHover: checked })}
        >
          <div className={"flex items-center gap-1"}>
            <ZoomInOutlined className={"text-base"} />
            {t<string>("resource.display.hoverLargeCover")}
          </div>
        </Checkbox>
      </div>

      <div>
        <Checkbox
          size="sm"
          isSelected={!resourceUiOptions?.disableMediaPreviewer}
          onValueChange={(checked) => patchOptions({ disableMediaPreviewer: !checked })}
        >
          <div className={"flex items-center gap-1"}>
            <PlayCircleOutlined className={"text-base"} />
            {t<string>("resource.display.previewFiles")}
          </div>
        </Checkbox>
      </div>

      <div>
        <Checkbox
          size="sm"
          isSelected={!!resourceUiOptions?.autoSelectFirstPlayableFile}
          onValueChange={(checked) => patchOptions({ autoSelectFirstPlayableFile: checked })}
        >
          <div className={"flex items-center gap-1"}>
            <PlayCircleOutlined className={"text-base"} />
            {t<string>("resource.display.autoSelectFile")}
          </div>
        </Checkbox>
      </div>

      <div className={"flex flex-col gap-1"}>
        <Checkbox
          size="sm"
          isSelected={resourceUiOptions?.inlineDisplayName}
          onValueChange={(checked) => patchOptions({ inlineDisplayName: checked })}
        >
          <div className={"flex items-center gap-1"}>
            <ImEmbed className={"text-base"} />
            {t<string>("resource.display.inlineNameTags")}
          </div>
        </Checkbox>
        <div className={"opacity-60 text-sm"}>
          {t<string>("resource.display.inlineNameTagsDesc")}
        </div>
      </div>

      <div className={"flex flex-col gap-1"}>
        <Checkbox
          size="sm"
          isSelected={!resourceUiOptions?.disableCache}
          onValueChange={(checked) => patchOptions({ disableCache: !checked })}
        >
          <div className={"flex items-center gap-1"}>
            <DatabaseOutlined className={"text-base"} />
            {t<string>("resource.display.useCache")}
          </div>
        </Checkbox>
        <div>
          <span className={"opacity-60 text-sm"}>
            {t<string>("resource.display.useCacheDesc")}
          </span>
          <Button
            color="primary"
            size={"sm"}
            variant={"light"}
            onClick={() => {
              navigate("/cache");
            }}
          >
            {t<string>("resource.display.manageCache")}
          </Button>
        </div>
      </div>

      <div>
        <Checkbox
          size="sm"
          isSelected={!resourceUiOptions?.disableCoverCarousel}
          onValueChange={(checked) => patchOptions({ disableCoverCarousel: !checked })}
        >
          <div className={"flex items-center gap-1"}>
            <BiCarousel className={"text-base"} />
            {t<string>("resource.display.coverCarousel")}
          </div>
        </Checkbox>
      </div>

      <div>
        <Checkbox
          size="sm"
          isSelected={!!resourceUiOptions?.displayResourceId}
          onValueChange={(checked) => patchOptions({ displayResourceId: checked })}
        >
          <div className={"flex items-center gap-1"}>
            <AiOutlineFieldNumber className={"text-base"} />
            {t<string>("resource.display.showResourceId")}
          </div>
        </Checkbox>
      </div>

      <div className={"flex items-center gap-2"}>
        <div className={"text-sm"}>{t("resource.display.filterOptionsCollapseThreshold")}</div>
        <Input
          type="number"
          size="sm"
          min={1}
          className="w-20"
          value={filterOptionsThreshold.toString()}
          onValueChange={(value) => {
            const num = parseInt(value, 10);
            if (!isNaN(num) && num > 0) {
              setFilterOptionsThreshold(num);
            }
          }}
        />
        <Button
          size="sm"
          variant="flat"
          onPress={() => setFilterOptionsThreshold(DEFAULT_FILTER_OPTIONS_THRESHOLD)}
        >
          {t("resource.display.reset")}
        </Button>
        <span className="text-xs text-default-400">
          {t("resource.display.filterOptionsCollapseThresholdDesc")}
        </span>
      </div>

      <Divider />

      <div className={"flex flex-col gap-2"}>
        <div className={"text-sm font-medium"}>
          {t<string>("resource.display.operations")}
        </div>
        <div className={"flex flex-wrap gap-2"}>
          <Checkbox
            size="sm"
            isSelected={
              resourceUiOptions?.displayOperations?.includes("aggregate") ??
              (resourceUiOptions?.displayOperations === undefined ||
                resourceUiOptions?.displayOperations === null ||
                resourceUiOptions?.displayOperations.length === 0)
            }
            onValueChange={(checked) => {
              const current = resourceUiOptions?.displayOperations ?? [];
              if (checked) {
                if (!current.includes("aggregate")) {
                  patchOptions({ displayOperations: [...current, "aggregate"] });
                }
              } else {
                patchOptions({
                  displayOperations: current.filter((op: string) => op !== "aggregate"),
                });
              }
            }}
          >
            <div className={"flex items-center gap-1"}>
              <ProductOutlined className={"text-base"} />
              {t<string>("resource.display.aggregateButton")}
            </div>
          </Checkbox>
          <Checkbox
            size="sm"
            isSelected={resourceUiOptions?.displayOperations?.includes("pin") ?? false}
            onValueChange={(checked) => {
              const current = resourceUiOptions?.displayOperations ?? [];
              if (checked) {
                if (!current.includes("pin")) {
                  patchOptions({ displayOperations: [...current, "pin"] });
                }
              } else {
                patchOptions({
                  displayOperations: current.filter((op: string) => op !== "pin"),
                });
              }
            }}
          >
            <div className={"flex items-center gap-1"}>
              <PushpinOutlined className={"text-base"} />
              {t<string>("resource.display.pinUnpin")}
            </div>
          </Checkbox>
          <Checkbox
            size="sm"
            isSelected={
              resourceUiOptions?.displayOperations?.includes("openFolder") ?? false
            }
            onValueChange={(checked) => {
              const current = resourceUiOptions?.displayOperations ?? [];
              if (checked) {
                if (!current.includes("openFolder")) {
                  patchOptions({ displayOperations: [...current, "openFolder"] });
                }
              } else {
                patchOptions({
                  displayOperations: current.filter((op: string) => op !== "openFolder"),
                });
              }
            }}
          >
            <div className={"flex items-center gap-1"}>
              <FolderOpenOutlined className={"text-base"} />
              {t<string>("resource.display.openFolder")}
            </div>
          </Checkbox>
          <Checkbox
            size="sm"
            isSelected={
              resourceUiOptions?.displayOperations?.includes("enhancements") ?? false
            }
            onValueChange={(checked) => {
              const current = resourceUiOptions?.displayOperations ?? [];
              if (checked) {
                if (!current.includes("enhancements")) {
                  patchOptions({ displayOperations: [...current, "enhancements"] });
                }
              } else {
                patchOptions({
                  displayOperations: current.filter((op: string) => op !== "enhancements"),
                });
              }
            }}
          >
            <div className={"flex items-center gap-1"}>
              <FireOutlined className={"text-base"} />
              {t<string>("resource.display.enhancements")}
            </div>
          </Checkbox>
          <Checkbox
            size="sm"
            isSelected={
              resourceUiOptions?.displayOperations?.includes("preview") ?? false
            }
            onValueChange={(checked) => {
              const current = resourceUiOptions?.displayOperations ?? [];
              if (checked) {
                if (!current.includes("preview")) {
                  patchOptions({ displayOperations: [...current, "preview"] });
                }
              } else {
                patchOptions({
                  displayOperations: current.filter((op: string) => op !== "preview"),
                });
              }
            }}
          >
            <div className={"flex items-center gap-1"}>
              <AiOutlinePicture className={"text-base"} />
              {t<string>("resource.display.preview")}
            </div>
          </Checkbox>
          <Checkbox
            size="sm"
            isSelected={
              resourceUiOptions?.displayOperations?.includes("addToPlaylist") ?? false
            }
            onValueChange={(checked) => {
              const current = resourceUiOptions?.displayOperations ?? [];
              if (checked) {
                if (!current.includes("addToPlaylist")) {
                  patchOptions({ displayOperations: [...current, "addToPlaylist"] });
                }
              } else {
                patchOptions({
                  displayOperations: current.filter((op: string) => op !== "addToPlaylist"),
                });
              }
            }}
          >
            <div className={"flex items-center gap-1"}>
              <VideoCameraAddOutlined className={"text-base"} />
              {t<string>("resource.display.addToPlaylist")}
            </div>
          </Checkbox>
        </div>
      </div>

      <Divider />
      <div className={"flex flex-col gap-1"}>
        <div className={"flex items-center gap-2"}>
          <AppstoreAddOutlined className={"text-base"} />
          <div className={"text-sm"}>{t<string>("resource.display.properties")}</div>
          <div className={"text-sm text-default-500"}>
            {t<string>("resource.display.selectedProperties")}: {resourceUiOptions?.displayProperties?.length ?? 0}
          </div>
          <Button
            size={"sm"}
            variant={"flat"}
            onPress={() => {
              const selection = (resourceUiOptions?.displayProperties ?? []).map((k) => ({
                id: k.id,
                pool: k.pool as any,
              }));

              createPortal(PropertySelector, {
                v2: true,
                selection,
                multiple: true,
                pool: PropertyPool.All,
                onSubmit: async (selected) => {
                  patchOptions({
                    displayProperties: selected.map((p: any) => ({ id: p.id, pool: p.pool })),
                  });
                },
              });
            }}
          >
            {t<string>("resource.display.selectProperties")}
          </Button>
        </div>
        <div className={"flex items-center flex-wrap gap-1"}>
          {resourceUiOptions?.displayProperties?.map((p) => {
            const property = propertyMap[p.pool as PropertyPool]?.[p.id];

            if (!property) {
              return null;
            }

            return (
              <div key={`${p.pool}-${p.id}`} className={"flex items-center gap-1"}>
                {property ? (
                  <BriefProperty property={property} fields={["pool", "name"]} showPoolChip={false} />
                ) : (
                  <Chip>{t("common.label.unknownProperty")}</Chip>
                )}
                <Button
                  isIconOnly
                  color="danger"
                  size={"sm"}
                  variant={"light"}
                  onPress={() => {
                    patchOptions({
                      displayProperties: resourceUiOptions?.displayProperties?.filter(
                        (pp) => pp.id !== p.id && pp.pool !== p.pool,
                      ),
                    });
                  }}
                >
                  <CloseOutlined />
                </Button>
              </div>
            );
          })}
        </div>
      </div>
    </div>
  );

  const renderStyleTab = () => (
    <div className={"flex flex-col gap-4"}>
      {cssVariableGroups.map((group) => (
        <div key={group.labelKey} className={"flex flex-col gap-2"}>
          <div className={"text-sm font-medium"}>
            {t<string>(group.labelKey)}
          </div>
          {group.variables.map((def) => {
            const storeValue = parseFloat(cssOverwrites[def.name] ?? def.defaultValue);
            const displayValue = localSliderValues[def.name] ?? storeValue;
            return (
              <div key={def.name} className={"flex items-center gap-4"}>
                <div className={"text-sm whitespace-nowrap shrink-0"}>
                  {t<string>(def.labelKey)}
                </div>
                <Slider
                  aria-label={t<string>(def.labelKey)}
                  minValue={def.min}
                  maxValue={def.max}
                  step={def.step}
                  value={displayValue}
                  size="sm"
                  className={"max-w-48"}
                  onChange={(value) => {
                    const v = Array.isArray(value) ? value[0] : value;
                    setLocalSliderValues((prev) => ({ ...prev, [def.name]: v }));
                    document.documentElement.style.setProperty(def.name, `${v}${def.unit}`);
                  }}
                  onChangeEnd={(value) => {
                    const v = Array.isArray(value) ? value[0] : value;
                    setLocalSliderValues((prev) => {
                      const next = { ...prev };
                      delete next[def.name];
                      return next;
                    });
                    uiStyleOptionsStore.patch({
                      cssVariableOverwrites: {
                        ...cssOverwrites,
                        [def.name]: `${v}${def.unit}`,
                      },
                    });
                  }}
                />
                <span className="text-xs text-default-400 shrink-0">
                  {displayValue}{def.unit}
                </span>
              </div>
            );
          })}
        </div>
      ))}
    </div>
  );

  return (
    <>
      <Button isIconOnly size={"sm"} variant={"light"} onPress={() => setVisible(true)}>
        <AiOutlineSetting className={"text-xl"} />
      </Button>
      {visible && (
        <Modal
          footer={false}
          size="4xl"
          title={t<string>("resource.display.title")}
          visible={visible}
          onClose={() => setVisible(false)}
        >
          <Tabs>
            <Tab key="general" title={t<string>("resource.display.tab.general")}>
              {renderGeneralTab()}
            </Tab>
            <Tab key="style" title={t<string>("resource.display.tab.style")}>
              {renderStyleTab()}
            </Tab>
          </Tabs>
        </Modal>
      )}
    </>
  );
};

MiscellaneousOptions.displayName = "MiscellaneousOptions";

export default MiscellaneousOptions;
