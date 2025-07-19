"use client";

import React, { useEffect, useState } from "react";

import "./index.scss";
import { useTranslation } from "react-i18next";
import {
  AppstoreOutlined,
  CloseCircleOutlined,
  DisconnectOutlined,
  FolderOpenOutlined,
  PlayCircleOutlined,
  ProfileOutlined,
  SettingOutlined,
} from "@ant-design/icons";
import _ from "lodash";
import { MdCalendarMonth } from "react-icons/md";
import { TiFlowChildren } from "react-icons/ti";

import ChildrenModal from "../ChildrenModal";

import BasicInfo from "./BasicInfo";
import Properties from "./Properties";

import ResourceCover from "@/components/Resource/components/ResourceCover";

import type { Resource as ResourceModel } from "@/core/models/Resource";

import {
  Button,
  ButtonGroup,
  Chip,
  Listbox,
  ListboxItem,
  Modal,
  Popover,
  Tooltip,
} from "@/components/bakaui";

import type { DestroyableProps } from "@/components/bakaui/types";

import BApi from "@/sdk/BApi";
import {
  PropertyPool,
  ReservedProperty,
  ResourceAdditionalItem,
} from "@/sdk/constants";
import { convertFromApiValue } from "@/components/StandardValue/helpers";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import PropertyValueScopePicker from "@/components/Resource/components/DetailDialog/PropertyValueScopePicker";
import PlayableFiles from "@/components/Resource/components/PlayableFiles";
import CategoryPropertySortModal from "@/components/Resource/components/DetailDialog/CategoryPropertySortModal";
import CustomPropertySortModal from "@/components/CustomPropertySortModal";
import { useUiOptionsStore } from "@/models/options";

interface Props extends DestroyableProps {
  id: number;
  onRemoved?: () => void;
}

export default ({ id, onRemoved, ...props }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [resource, setResource] = useState<ResourceModel>();
  const uiOptions = useUiOptionsStore((state) => state.data);

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
              v.aliasAppliedBizValue = convertFromApiValue(
                v.aliasAppliedBizValue,
                b.bizValueType!,
              );
              v.value = convertFromApiValue(v.value, b.dbValueType!);
            }
          }
        });
      });
    }
    // @ts-ignore
    setResource(d);
  };

  useEffect(() => {
    loadResource();
  }, []);

  console.log(resource);

  const hideTimeInfo = !!uiOptions.resource?.hideResourceTimeInfo;

  return (
    <Modal
      defaultVisible
      footer={false}
      size={"xl"}
      title={
        <div className={"flex items-center justify-between"}>
          <div>{resource?.displayName}</div>
          <Popover
            shouldCloseOnBlur
            placement={"left-start"}
            trigger={
              <Button
                isIconOnly
                size={"sm"}
                variant={"light"}
                // className={'absolute left-0 top-0'}
              >
                <SettingOutlined className={"text-base"} />
              </Button>
            }
          >
            <Listbox
              aria-label="Actions"
              onAction={(key) => {
                switch (key) {
                  case "AdjustPropertyScopePriority": {
                    createPortal(PropertyValueScopePicker, {
                      resource,
                    });
                    break;
                  }
                  case "SortPropertiesInCategory": {
                    const propertyMap =
                      resource?.properties?.[PropertyPool.Custom] ?? {};
                    const properties = _.keys(propertyMap)
                      .map((x) => {
                        const id = parseInt(x, 10);
                        const p = propertyMap[id]!;

                        if (p.visible) {
                          return {
                            id,
                            name: p.name!,
                          };
                        }

                        return null;
                      })
                      .filter((x) => x != null);

                    createPortal(CategoryPropertySortModal, {
                      categoryId: resource.categoryId,
                      properties,
                      onDestroyed: loadResource,
                    });
                    break;
                  }
                  case "SortPropertiesGlobally": {
                    BApi.customProperty.getAllCustomProperties().then((r) => {
                      const properties = (r.data || []).sort(
                        (a, b) => a.order - b.order,
                      );

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
                {t<string>("Adjust the display priority of property scopes")}
              </ListboxItem>
              <ListboxItem
                key="SortPropertiesInCategory"
                startContent={<ProfileOutlined className={"text-small"} />}
              >
                {t<string>(
                  "Adjust orders of linked properties for current category",
                )}
              </ListboxItem>
              <ListboxItem
                key="SortPropertiesGlobally"
                startContent={<ProfileOutlined className={"text-small"} />}
              >
                {t<string>("Adjust orders of properties globally")}
              </ListboxItem>
            </Listbox>
          </Popover>
        </div>
      }
      onDestroyed={props.onDestroyed}
    >
      {resource && (
        <>
          <div className="flex gap-4">
            <div className="min-w-[400px] w-[400px] max-w-[400px] flex flex-col gap-4">
              <div
                className={
                  "h-[400px] max-h-[400px] overflow-hidden rounded flex items-center justify-center border-1"
                }
                style={{ borderColor: "var(--bakaui-overlap-background)" }}
              >
                <ResourceCover
                  resource={resource}
                  showBiggerOnHover={false}
                  useCache={false}
                />
              </div>
              <div className={"flex justify-center"}>
                <Properties
                  hidePropertyName
                  propertyInnerDirection={"ver"}
                  reload={loadResource}
                  resource={resource}
                  restrictedPropertyIds={[ReservedProperty.Rating]}
                  restrictedPropertyPool={PropertyPool.Reserved}
                />
              </div>
              <div className={"flex items-center justify-between relative"}>
                <div
                  className={
                    "absolute flex justify-center left-0 right-0 w-full "
                  }
                >
                  <ButtonGroup size={"sm"}>
                    <PlayableFiles
                      autoInitialize
                      PortalComponent={({ onClick }) => (
                        <Button color="primary" onPress={onClick}>
                          <PlayCircleOutlined />
                          {t<string>("Play")}
                        </Button>
                      )}
                      resource={resource}
                    />
                    <Button
                      color="default"
                      onPress={() => {
                        BApi.resource.openResourceDirectory({
                          id: resource.id,
                        });
                      }}
                    >
                      <FolderOpenOutlined />
                      {t<string>("Open")}
                    </Button>
                    {resource.hasChildren && (
                      <Button
                        color="default"
                        onPress={() => {
                          createPortal(ChildrenModal, {
                            resourceId: resource.id,
                          });
                        }}
                      >
                        <TiFlowChildren className="text-lg" />
                        {t<string>("View Children")}
                      </Button>
                    )}
                    {/* <Button */}
                    {/*   color={'danger'} */}
                    {/*   onClick={() => onRemoved?.()} */}
                    {/* > */}
                    {/*   <DeleteOutlined /> */}
                    {/*   {t<string>('Remove')} */}
                    {/* </Button> */}
                  </ButtonGroup>
                </div>
                <div />
                <div>
                  <Button
                    isIconOnly
                    className={hideTimeInfo ? "opacity-20" : undefined}
                    color="default"
                    size={"sm"}
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
                </div>
              </div>
              {!hideTimeInfo && <BasicInfo resource={resource} />}
            </div>
            <div className="overflow-auto relative grow">
              <Properties
                noPropertyContent={
                  <div
                    className={
                      "flex flex-col items-center gap-2 justify-center"
                    }
                  >
                    <div className={"w-4/5"}>
                      <DisconnectOutlined className={"text-base mr-1"} />
                      {t<string>(
                        "No custom property bound. You can bind them in media library template",
                      )}
                    </div>
                  </div>
                }
                propertyClassNames={{
                  name: "justify-end",
                }}
                reload={loadResource}
                resource={resource}
                restrictedPropertyPool={PropertyPool.Custom}
              />
              <div className={"flex flex-col gap-1 mt-2"}>
                <Properties
                  propertyClassNames={{
                    name: "justify-end",
                  }}
                  reload={loadResource}
                  resource={resource}
                  restrictedPropertyIds={[ReservedProperty.Cover]}
                  restrictedPropertyPool={PropertyPool.Reserved}
                />
                {resource.playedAt && (
                  <div
                    className={
                      "grid gap-x-4 gap-y-1 undefined items-center overflow-visible"
                    }
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
                      {t<string>("Last played at")}
                    </Chip>
                    <div className={"flex items-center gap-1"}>
                      {resource.playedAt}
                      <Tooltip content={t<string>("Mark as not played")}>
                        <Button
                          isIconOnly
                          size={"sm"}
                          variant={"light"}
                          onPress={() => {
                            BApi.resource
                              .markResourceAsNotPlayed(resource.id)
                              .then((r) => {
                                if (!r.code) {
                                  loadResource();
                                }
                              });
                          }}
                        >
                          <CloseCircleOutlined
                            className={"text-base opacity-60"}
                          />
                        </Button>
                      </Tooltip>
                    </div>
                  </div>
                )}
              </div>
            </div>
          </div>
          <div className={"mt-2"}>
            <Properties
              propertyInnerDirection={"ver"}
              reload={loadResource}
              resource={resource}
              restrictedPropertyIds={[ReservedProperty.Introduction]}
              restrictedPropertyPool={PropertyPool.Reserved}
            />
          </div>
          {/* <Divider /> */}
          {/* <FileSystemEntries isFile={resource.isFile} path={resource.path} /> */}
        </>
      )}
    </Modal>
  );
};
