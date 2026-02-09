"use client";

import React, { useEffect, useState } from "react";
import { useLocalStorage } from "react-use";

import "./index.scss";
import { useTranslation } from "react-i18next";
import {
  AppstoreOutlined,
  CloseCircleOutlined,
  DisconnectOutlined,
  FolderOpenOutlined,
  LoadingOutlined,
  PlayCircleOutlined,
  ProfileOutlined,
  QuestionCircleOutlined,
  SettingOutlined,
} from "@ant-design/icons";
import { MdCalendarMonth } from "react-icons/md";
import { TiFlowChildren } from "react-icons/ti";
import { TbColumns1, TbColumns2, TbColumns3 } from "react-icons/tb";

import ChildrenModal from "../ChildrenModal";

import BasicInfo from "./BasicInfo";
import Properties from "./Properties";
import MediaLibraryMappings from "./MediaLibraryMappings";
import IntroductionSummary from "./IntroductionSummary";
import ResourceProfiles from "./ResourceProfiles";
import ResourceHierarchy from "./ResourceHierarchy";

import ResourceCover from "@/components/Resource/components/ResourceCover";

import type { Resource as ResourceModel } from "@/core/models/Resource";

import {
  Button,
  ButtonGroup,
  Chip,
  Divider,
  Listbox,
  ListboxItem,
  Modal,
  Popover,
  Spinner,
  Tooltip,
} from "@/components/bakaui";

import type { DestroyableProps } from "@/components/bakaui/types";

import BApi from "@/sdk/BApi";
import { PropertyPool, ReservedProperty, ResourceAdditionalItem } from "@/sdk/constants";
import { convertFromApiValue } from "@/components/StandardValue/helpers";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertyValueScopePicker from "@/components/Resource/components/DetailModal/PropertyValueScopePicker";
import PlayControl from "@/components/Resource/components/PlayControl";
import type { PlayControlPortalProps } from "@/components/Resource/components/PlayControl";
import CustomPropertySortModal from "@/components/CustomPropertySortModal";
import { useUiOptionsStore } from "@/stores/options";

type ColumnCount = 1 | 2 | 3;
const PROPERTIES_COLUMNS_KEY = "bakabase:properties:columns";

interface Props extends DestroyableProps {
  id: number;
  initialResource?: ResourceModel;
  onRemoved?: () => void;
}
const DetailModal = ({ id, initialResource, onRemoved, ...props }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [resource, setResource] = useState<ResourceModel | undefined>(initialResource);
  const [loading, setLoading] = useState(!initialResource);
  const uiOptions = useUiOptionsStore((state) => state.data);
  const [columns, setColumns] = useLocalStorage<ColumnCount>(PROPERTIES_COLUMNS_KEY, 2);

  const loadResource = async () => {
    // @ts-ignore
    const r = await BApi.resource.getResourcesByKeys({
      ids: [id],
      additionalItems: ResourceAdditionalItem.All,
    });
    const d = (r.data || [])?.[0] ?? {};

    if (d.properties) {
      Object.values(d.properties).forEach((a) => {
        Object.values(a).forEach((b) => {
          if (b.values) {
            for (const v of b.values) {
              v.bizValue = convertFromApiValue(v.bizValue, b.bizValueType!);
              v.aliasAppliedBizValue = convertFromApiValue(v.aliasAppliedBizValue, b.bizValueType!);
              v.value = convertFromApiValue(v.value, b.dbValueType!);
            }
          }
        });
      });
    }
    // @ts-ignore
    setResource(d);
    setLoading(false);
  };

  useEffect(() => {
    // Always load full data, even if initialResource is provided
    // initialResource may be a basic resource without complete properties
    loadResource();
  }, []);

  console.log(resource);

  const hideTimeInfo = !!uiOptions.resource?.hideResourceTimeInfo;

  return (
    <Modal
      defaultVisible
      footer={false}
      size={"7xl"}
      title={
        <div className={"flex items-start justify-between gap-4 flex-wrap"}>
          {/* Left side: Title (allow wrapping) */}
          <div className="flex-shrink min-w-0 break-words">{resource?.displayName}</div>
          {/* Right side: Media Libraries, Profiles, and Settings */}
          <div className="flex items-center gap-3 flex-shrink-0">
            {resource && (
              <>
                <MediaLibraryMappings
                  compact
                  resourceId={resource.id}
                  onMappingsChange={loadResource}
                />
                <ResourceProfiles compact resourceId={resource.id} />
              </>
            )}
            <Popover
              shouldCloseOnBlur
              placement={"left-start"}
              trigger={
                <Button
                  isIconOnly
                  size={"sm"}
                  variant={"light"}
                >
                  <SettingOutlined className={"text-base"} />
                </Button>
              }
            >
              <div className="flex flex-col gap-2 p-2">
                <div className="flex items-center gap-2">
                  <span className="text-sm text-default-500">{t("resource.label.columns")}</span>
                  <ButtonGroup size="sm">
                    {([
                      { col: 1 as const, icon: <TbColumns1 /> },
                      { col: 2 as const, icon: <TbColumns2 /> },
                      { col: 3 as const, icon: <TbColumns3 /> },
                    ]).map(({ col, icon }) => (
                      <Button
                        key={col}
                        isIconOnly
                        color={columns === col ? "primary" : "default"}
                        variant={columns === col ? "solid" : "flat"}
                        onPress={() => setColumns(col)}
                      >
                        {icon}
                      </Button>
                    ))}
                  </ButtonGroup>
                </div>
                <Divider />
                <Listbox
                  aria-label="Actions"
                  onAction={(key) => {
                    switch (key) {
                      case "AdjustPropertyScopePriority": {
                        if (resource) {
                          createPortal(PropertyValueScopePicker, {
                            resource,
                          });
                        }
                        break;
                      }
                      case "SortPropertiesGlobally": {
                        BApi.customProperty.getAllCustomProperties().then((r) => {
                          const properties = (r.data || []).sort((a, b) => a.order - b.order);

                          createPortal(CustomPropertySortModal, {
                            properties,
                            onDestroyed: loadResource,
                          });
                        });
                      }
                    }
                  }}
                >
                  <ListboxItem
                    key="AdjustPropertyScopePriority"
                    startContent={<AppstoreOutlined className={"text-small"} />}
                  >
                    {t<string>("resource.action.adjustPropertyScopePriority")}
                  </ListboxItem>
                  <ListboxItem
                    key="SortPropertiesGlobally"
                    startContent={<ProfileOutlined className={"text-small"} />}
                  >
                    {t<string>("resource.action.sortPropertiesGlobally")}
                  </ListboxItem>
                </Listbox>
              </div>
            </Popover>
          </div>
        </div>
      }
      onDestroyed={props.onDestroyed}
    >
      {resource && (
        <>
          <div className="flex gap-4 pb-2">
            <div className="min-w-[400px] w-[400px] max-w-[400px] flex flex-col gap-4">
              <div
                className={
                  "h-[400px] max-h-[400px] overflow-hidden rounded flex items-center justify-center border-1"
                }
                style={{ borderColor: "var(--bakaui-overlap-background)" }}
              >
                <ResourceCover resource={resource} showBiggerOnHover={false} />
              </div>
              <div className={"flex items-center"}>
                <div className="flex-1" />
                <Properties
                  hidePropertyName
                  propertyInnerDirection={"ver"}
                  reload={loadResource}
                  resource={resource}
                  restrictedPropertyIds={[ReservedProperty.Rating]}
                  restrictedPropertyPool={PropertyPool.Reserved}
                />
                <div className="flex-1 flex justify-end">
                  <ButtonGroup size={"sm"}>
                    <PlayControl
                      PortalComponent={({ status, onClick, tooltipContent }: PlayControlPortalProps) => (
                        <Tooltip content={tooltipContent ?? t("common.action.play")}>
                          <Button
                            isIconOnly
                            color="primary"
                            onPress={onClick}
                            isDisabled={status === "loading"}
                          >
                            {status === "loading" ? (
                              <LoadingOutlined className="text-lg" spin />
                            ) : status === "not-found" ? (
                              <QuestionCircleOutlined className="text-lg text-warning" />
                            ) : (
                              <PlayCircleOutlined className="text-lg" />
                            )}
                          </Button>
                        </Tooltip>
                      )}
                      resource={resource}
                    />
                    <Tooltip content={t("common.action.openFolder")}>
                      <Button
                        isIconOnly
                        color="default"
                        variant="light"
                        onPress={() => {
                          BApi.resource.openResourceDirectory({
                            id: resource.id,
                          });
                        }}
                      >
                        <FolderOpenOutlined className="text-lg" />
                      </Button>
                    </Tooltip>
                    {resource.hasChildren && (
                      <Tooltip content={t("common.action.viewChildren")}>
                        <Button
                          isIconOnly
                          color="default"
                          onPress={() => {
                            createPortal(ChildrenModal, {
                              resourceId: resource.id,
                              resourceDisplayName: resource.displayName,
                            });
                          }}
                        >
                          <TiFlowChildren className="text-lg" />
                        </Button>
                      </Tooltip>
                    )}
                    <Tooltip content={hideTimeInfo ? t("common.action.showTimeInfo") : t("common.action.hideTimeInfo")}>
                      <Button
                        isIconOnly
                        className={hideTimeInfo ? "opacity-40" : undefined}
                        color="default"
                        variant={"light"}
                        onPress={() => {
                          BApi.options.patchUiOptions({
                            ...uiOptions,
                            resource: {
                              ...uiOptions.resource,
                              hideResourceTimeInfo: !hideTimeInfo,
                            },
                          });
                        }}
                      >
                        <MdCalendarMonth className={"text-lg"} />
                      </Button>
                    </Tooltip>
                  </ButtonGroup>
                </div>
              </div>
              {!hideTimeInfo && <BasicInfo resource={resource} />}
            </div>
            <div className="overflow-auto relative grow">
              {loading ? (
                <div className="flex items-center justify-center h-full min-h-[200px]">
                  <Spinner label={t<string>("common.state.loadingProperties")} />
                </div>
              ) : (
                <div className="flex flex-col gap-1">
                  <ResourceHierarchy resource={resource} onReload={loadResource} />
                  <IntroductionSummary resource={resource} onReload={loadResource} />
                  <div className={"flex flex-col gap-1"}>
                    <Properties
                      columns={1}
                      reload={loadResource}
                      resource={resource}
                      restrictedPropertyIds={[ReservedProperty.Cover]}
                      restrictedPropertyPool={PropertyPool.Reserved}
                    />
                    {resource.playedAt && (
                      <div
                        className={"grid gap-x-4 gap-y-1 undefined items-center overflow-visible"}
                        style={{
                          gridTemplateColumns: "calc(120px) minmax(0, 1fr)",
                        }}
                      >
                        <Chip
                          className={"text-right justify-self-end"}
                          color={"default"}
                          radius={"sm"}
                          size={"sm"}
                        >
                          {t<string>("resource.label.lastPlayedAt")}
                        </Chip>
                        <div className={"flex items-center gap-1"}>
                          {resource.playedAt}
                          <Tooltip content={t<string>("resource.action.markAsNotPlayed")}>
                            <Button
                              isIconOnly
                              size={"sm"}
                              variant={"light"}
                              onPress={() => {
                                BApi.resource.markResourceAsNotPlayed(resource.id).then((r) => {
                                  if (!r.code) {
                                    loadResource();
                                  }
                                });
                              }}
                            >
                              <CloseCircleOutlined className={"text-base opacity-60"} />
                            </Button>
                          </Tooltip>
                        </div>
                      </div>
                    )}
                  </div>
                  <Properties
                    columns={columns}
                    noPropertyContent={
                      <div className={"flex flex-col items-center gap-2 justify-center"}>
                        <div className={"w-4/5"}>
                          <DisconnectOutlined className={"text-base mr-1"} />
                          {t<string>("resource.empty.noCustomPropertyBound")}
                        </div>
                      </div>
                    }
                    reload={loadResource}
                    resource={resource}
                    restrictedPropertyPool={PropertyPool.Custom}
                  />
                </div>
              )}
            </div>
          </div>
          {/* <Divider /> */}
          {/* <FileSystemEntries isFile={resource.isFile} path={resource.path} /> */}
        </>
      )}
    </Modal>
  );
};

DetailModal.displayName = "DetailModal";

export default DetailModal;
