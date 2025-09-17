"use client";

import type { BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions } from "@/sdk/Api";

import {
  AppstoreAddOutlined,
  CloseOutlined,
  DashboardOutlined,
  DatabaseOutlined,
  FullscreenOutlined,
  PlayCircleOutlined,
  QuestionCircleOutlined,
  ZoomInOutlined,
} from "@ant-design/icons";
import React, { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { AiOutlineFieldNumber, AiOutlineOrderedList, AiOutlineSetting } from "react-icons/ai";
import { BiCarousel } from "react-icons/bi";

import { Button, Checkbox, Chip, Divider, Modal, Spacer, Tooltip, ButtonGroup } from "@/components/bakaui";
import { CoverFit, PropertyPool } from "@/sdk/constants";
import BApi from "@/sdk/BApi";
import { useUiOptionsStore } from "@/stores/options";
import { buildLogger } from "@/components/utils";
import PropertySelector from "@/components/PropertySelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { PropertyMap } from "@/components/types";
import _ from "lodash";
import { ImEmbed } from "react-icons/im";
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
    const psr =
      (await BApi.property.getPropertiesByPool(PropertyPool.All)).data || [];
    const ps = _.mapValues(
      _.groupBy(psr, (x) => x.pool),
      (v) => _.keyBy(v, (x) => x.id),
    );

    setPropertyMap(ps);
  };

  useEffect(() => {
    loadProperties();
  }, [])

  const [visible, setVisible] = useState(false);

  const patchOptions = async (options: Partial<BakabaseInsideWorldModelsConfigsUIOptionsUIResourceOptions>) => {
    await uiOptionsStore.patch({ resource: { ...resourceUiOptions, ...options } });
  };

  return (
    <>
      <Button isIconOnly size={"sm"} variant={"light"} onPress={() => setVisible(true)}>
        <AiOutlineSetting className={"text-xl"} />
      </Button>
      {visible && (
        <Modal
          size="lg"
          title={t<string>("Display options")}
          visible={visible}
          onClose={() => setVisible(false)}
          footer={false}
        >
          <div className={"flex flex-col gap-3"}>
            <div className={"flex items-center gap-2"}>
              <div className={"text-sm"}>{t<string>("Column count")}</div>
              <div className={"flex flex-wrap gap-1"}>
                <ButtonGroup>
                  {Array.from({ length: (MaxResourceColCount - MinResourceColCount + 1) }, (_, idx) => idx + MinResourceColCount).map((num) => (
                    <Button
                      key={num}
                      className={"min-w-0 px-6"}
                      size={"sm"}
                      color={currentColCount === num ? "primary" : "default"}
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
                isSelected={!!resourceUiOptions?.showBiggerCoverWhileHover}
                onValueChange={(checked) =>
                  patchOptions({ showBiggerCoverWhileHover: checked })
                }
              >
                <div className={"flex items-center gap-1"}>
                  <ZoomInOutlined className={"text-base"} />
                  {t<string>("Show larger cover on mouse hover")}
                </div>
              </Checkbox>
            </div>

            <div>
              <Checkbox
                isSelected={!resourceUiOptions?.disableMediaPreviewer}
                onValueChange={(checked) =>
                  patchOptions({ disableMediaPreviewer: !checked })
                }
              >
                <div className={"flex items-center gap-1"}>
                  <PlayCircleOutlined className={"text-base"} />
                  {t<string>("Preview files of a resource on mouse hover")}
                </div>
              </Checkbox>
            </div>

            <div className={"flex flex-col gap-1"}>
              <Checkbox
                isSelected={resourceUiOptions?.inlineDisplayName}
                onValueChange={(checked) =>
                  patchOptions({ inlineDisplayName: checked })
                }
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
                isSelected={!resourceUiOptions?.disableCache}
                onValueChange={(checked) =>
                  patchOptions({ disableCache: !checked })
                }
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
                  size={"sm"}
                  variant={"light"}
                  color="primary"
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
                isSelected={!resourceUiOptions?.disableCoverCarousel}
                onValueChange={(checked) =>
                  patchOptions({ disableCoverCarousel: !checked })
                }
              >
                <div className={"flex items-center gap-1"}>
                  <BiCarousel className={"text-base"} />
                  {t<string>("Cover carousel")}
                </div>
              </Checkbox>
            </div>

            <div>
              <Checkbox
                isSelected={!!resourceUiOptions?.displayResourceId}
                onValueChange={(checked) =>
                  patchOptions({ displayResourceId: checked })
                }
              >
                <div className={"flex items-center gap-1"}>
                  <AiOutlineFieldNumber className={"text-base"} />
                  {t<string>("Display resource ID")}
                </div>
              </Checkbox>
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
                    const selection = (resourceUiOptions?.displayProperties ?? []).map((k) => ({ id: k.id, pool: k.pool as any }));
                    createPortal(PropertySelector, {
                      selection,
                      multiple: true,
                      pool: PropertyPool.All,
                      onSubmit: async (selected) => {
                        patchOptions({ displayProperties: selected.map((p: any) => ({ id: p.id, pool: p.pool })) })
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
                    <div className={"flex items-center gap-1"} key={`${p.pool}-${p.id}`}>
                      {property ? (
                        <PropertyLabel
                          property={property}
                          showPool={true}
                        />
                      ) : (
                        <Chip>{t("Unknown property")}</Chip>
                      )}
                      <Button color="danger" isIconOnly size={"sm"} variant={"light"} onPress={() => {
                        patchOptions({ displayProperties: resourceUiOptions?.displayProperties?.filter((pp) => pp.id !== p.id && pp.pool !== p.pool) })
                      }}>
                        <CloseOutlined />
                      </Button>
                    </div>
                  )
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
