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

import { Button, Checkbox, Chip, Divider, Modal, ButtonGroup } from "@/components/bakaui";
import { CoverFit, PropertyPool } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useUiOptionsStore } from "@/stores/options";
import { buildLogger } from "@/components/utils";
import PropertySelector from "@/components/PropertySelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { PropertyLabel } from "@/components/Property";

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
  const resourceUiOptions = uiOptionsStore.data?.resource;
  const currentColCount = resourceUiOptions?.colCount ?? DefaultResourceColCount;
  const [propertyMap, setPropertyMap] = useState<PropertyMap>({});

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

  return (
    <>
      <Button isIconOnly size={"sm"} variant={"light"} onPress={() => setVisible(true)}>
        <AiOutlineSetting className={"text-xl"} />
      </Button>
      {visible && (
        <Modal
          footer={false}
          size="lg"
          title={t<string>("Display options")}
          visible={visible}
          onClose={() => setVisible(false)}
        >
          <div className={"flex flex-col gap-1"}>
            <div className={"flex items-center gap-1"}>
              <div className={"text-sm"}>{t<string>("Column count")}</div>
              <div className={"flex flex-wrap gap-1"}>
                <ButtonGroup>
                  {Array.from(
                    { length: MaxResourceColCount - MinResourceColCount + 1 },
                    (_, idx) => idx + MinResourceColCount,
                  ).map((num) => (
                    <Button
                      key={num}
                      className={"min-w-0 px-6"}
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
                  {t<string>("Fill cover")}
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
                  {t<string>("Show larger cover on mouse hover")}
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
                  {t<string>("Preview files of a resource on mouse hover")}
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
                  {t<string>("Auto-select first file if multiple playable files exist")}
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
                  {t<string>("Inline display name and tags")}
                </div>
              </Checkbox>
              <div className={"opacity-60 text-sm"}>
                {t<string>(
                  "Inline the display name and tags will place them inside the bottom area of the resource cover, rather than below the cover.",
                )}
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
                  {t<string>("Use cache")}
                </div>
              </Checkbox>
              <div>
                <span className={"opacity-60 text-sm"}>
                  {t<string>(
                    "Enabling cache can improve loading speed, but your covers and playable files will not be updated in time unless you clear or disable cache manually.",
                  )}
                </span>
                <Button
                  color="primary"
                  size={"sm"}
                  variant={"light"}
                  onClick={() => {
                    navigate("/cache");
                  }}
                >
                  {t<string>("Manage cache")}
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
                  {t<string>("Cover carousel")}
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
                  {t<string>("Display resource ID")}
                </div>
              </Checkbox>
            </div>

            <Divider />

            <div className={"flex flex-col gap-2"}>
              <div className={"text-sm font-medium"}>
                {t<string>("Display operations")}
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
                    {t<string>("Aggregate button")}
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
                    {t<string>("Pin/Unpin")}
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
                    {t<string>("Open folder")}
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
                    {t<string>("Enhancements")}
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
                    {t<string>("Preview")}
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
                    {t<string>("Add to playlist")}
                  </div>
                </Checkbox>
              </div>
            </div>

            <Divider />
            <div className={"flex flex-col gap-1"}>
              <div className={"flex items-center gap-2"}>
                <AppstoreAddOutlined className={"text-base"} />
                <div className={"text-sm"}>{t<string>("Display properties")}</div>
                <div className={"text-sm text-default-500"}>
                  {t<string>("Selected")}: {resourceUiOptions?.displayProperties?.length ?? 0}
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
                  {t<string>("Select properties")}
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
                        <PropertyLabel property={property} showPool={true} />
                      ) : (
                        <Chip>{t("Unknown property")}</Chip>
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
        </Modal>
      )}
    </>
  );
};

MiscellaneousOptions.displayName = "MiscellaneousOptions";

export default MiscellaneousOptions;
