"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { Enhancement } from "@/components/Enhancer/models";
import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  ApiOutlined,
  ExclamationCircleOutlined,
  QuestionCircleOutlined,
  SyncOutlined,
} from "@ant-design/icons";
import { Dayjs } from "dayjs";

import BApi from "@/sdk/BApi";
import { createPortalOfComponent } from "@/components/utils";
import {
  Button,
  Chip,
  Modal,
  Snippet,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tabs,
  Tooltip,
} from "@/components/bakaui";
import {
  EnhancementAdditionalItem,
  EnhancementRecordStatus,
  PropertyPool,
  ReservedProperty,
} from "@/sdk/constants";
import CategoryEnhancerOptionsDialog from "@/components/EnhancerSelectorV2/components/CategoryEnhancerOptionsDialog";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import {
  convertFromApiValue,
  serializeStandardValue,
} from "@/components/StandardValue/helpers";

interface Props extends DestroyableProps {
  resourceId: number;
}

type ResourceEnhancements = {
  enhancer: EnhancerDescriptor;
  contextCreatedAt?: string;
  contextAppliedAt?: string;
  status: EnhancementRecordStatus;
  targets: {
    target: number;
    targetName: string;
    enhancement: Enhancement;
  }[];
  dynamicTargets: {
    target: number;
    targetName: string;
    enhancements: Enhancement[];
  }[];
};

function ResourceEnhancementsDialog({ resourceId, ...props }: Props) {
  const [enhancements, setEnhancements] = useState<ResourceEnhancements[]>([]);
  const [resource, setResource] = useState<{
    path: string;
    categoryId: number;
  }>({
    path: "",
    categoryId: 0,
  });
  const { t } = useTranslation();
  const [enhancing, setEnhancing] = useState(false);
  const [applyingContext, setApplyingContext] = useState(false);

  useEffect(() => {
    loadEnhancements();

    BApi.resource.getResourcesByKeys({ ids: [resourceId] }).then((r) => {
      const data = r.data || [];

      setResource({
        path: data[0]?.path || "",
        categoryId: data[0]?.categoryId ?? 0,
      });
    });
  }, []);

  const loadEnhancements = useCallback(async () => {
    const r = await BApi.resource.getResourceEnhancements(resourceId, {
      additionalItem: EnhancementAdditionalItem.GeneratedPropertyValue,
    });
    const data = r.data || [];

    for (const d of data) {
      d.dynamicTargets?.forEach((dt) => {
        dt.enhancements?.forEach((e) => {
          e.value = convertFromApiValue(e.value, e.valueType!);
          const v = e.customPropertyValue;
          const p = e.property;

          if (p && v) {
            v.value = convertFromApiValue(v.value, p.dbValueType!);
            v.bizValue = convertFromApiValue(v.bizValue, p.bizValueType!);
          }
        });
      });
    }

    // @ts-ignore
    setEnhancements(data);
  }, []);

  console.log(enhancements);

  const renderConvertedValue = (e: Enhancement | undefined) => {
    if (
      !e ||
      e.propertyPool == undefined ||
      e.propertyId == undefined ||
      e.property == undefined
    ) {
      return;
    }

    const { property } = e;

    switch (e.propertyPool) {
      case PropertyPool.Reserved: {
        const rv = e.reservedPropertyValue;

        if (!rv) {
          return;
        }
        let bizRv: any;
        let dbRv: any;

        switch (e.propertyId as ReservedProperty) {
          case ReservedProperty.Introduction:
            bizRv = rv.introduction;
            dbRv = rv.introduction;
            break;
          case ReservedProperty.Rating:
            bizRv = rv.rating;
            dbRv = rv.rating;
            break;
          case ReservedProperty.Cover:
            bizRv = rv.coverPaths;
            dbRv = rv.coverPaths;
            break;
          case ReservedProperty.PlayedAt:
            bizRv = new Dayjs(rv.playedAt);
            dbRv = new Dayjs(rv.playedAt);
            break;
          default:
            return t<string>("Unsupported reserved property type: {{type}}", {
              type: e.propertyId,
            });
        }

        return (
          <PropertyValueRenderer
            bizValue={serializeStandardValue(bizRv, property.bizValueType)}
            dbValue={serializeStandardValue(dbRv, property.dbValueType)}
            property={property}
            variant={"light"}
          />
        );
      }
      case PropertyPool.Custom: {
        const pv = e.customPropertyValue;

        if (!pv) {
          return;
        }

        return (
          <PropertyValueRenderer
            bizValue={serializeStandardValue(
              pv.bizValue,
              property!.bizValueType,
            )}
            dbValue={serializeStandardValue(pv.value, property!.dbValueType)}
            property={property}
            variant={"light"}
          />
        );
      }
      case PropertyPool.Internal:
      case PropertyPool.All:
        return t<string>("Unsupported property type: {{type}}", {
          type: e.propertyPool,
        });
    }
  };

  return (
    <Modal
      defaultVisible
      footer={{
        actions: ["ok"],
      }}
      size={"full"}
      title={t<string>("Enhancement records")}
      onDestroyed={props.onDestroyed}
    >
      <div className={"flex items-center gap-2"}>
        <div>{t<string>("Path of resource")}</div>
        <Snippet size={"sm"} symbol={""}>
          {resource.path}
        </Snippet>
      </div>
      <Tabs
        isVertical
        aria-label="Enhancers"
        classNames={{
          panel: "grow min-w-0",
        }}
        variant={"bordered"}
      >
        {enhancements.map((e) => {
          const { targets } = e;

          return (
            <Tab key={e.enhancer.id} title={e.enhancer.name}>
              <div className={"flex items-center justify-between"}>
                <div className={"flex items-center gap-2"}>
                  <div className={"flex items-center gap-2"}>
                    <Chip
                      // size={'sm'}
                      color={"secondary"}
                      radius={"sm"}
                      variant={"light"}
                    >
                      <div className={"flex items-center gap-1"}>
                        {t<string>("Data created at")}
                        <Tooltip
                          className={"max-w-[500px]"}
                          color={"secondary"}
                          content={t<string>(
                            "The data has been created, indicating that the enhancer has completed the data retrieval process, which is typically done by accessing third-party sites or executing specific internal logic. You can check the status of the data retrieval in the table below. Frequent repeated data retrieval attempts may result in access denial from third-party services.",
                          )}
                          placement={"top"}
                        >
                          <QuestionCircleOutlined className={"text-base"} />
                        </Tooltip>
                      </div>
                    </Chip>
                    {e.contextCreatedAt ?? t<string>("None")}
                  </div>
                  <div className={"flex items-center gap-2"}>
                    <Chip
                      // size={'sm'}
                      color={"secondary"}
                      radius={"sm"}
                      variant={"light"}
                    >
                      <div className={"flex items-center gap-1"}>
                        {t<string>("Data applied at")}
                        <Tooltip
                          className={"max-w-[500px]"}
                          color={"secondary"}
                          content={t<string>(
                            "The application of data indicates that the retrieved data has been successfully converted into attribute values. This step is conducted entirely within the program, without involving any third-party data exchange.",
                          )}
                          placement={"top"}
                        >
                          <QuestionCircleOutlined className={"text-base"} />
                        </Tooltip>
                      </div>
                    </Chip>
                    {e.contextAppliedAt ?? t<string>("None")}
                  </div>
                  <Tooltip
                    className={"max-w-[500px]"}
                    color={"secondary"}
                    content={t<string>(
                      "Retrieve data then apply data. To reduce the possibility of access denial from third-party services, it is recommended to apply data only after all data has been retrieved. Configuring enhancer options in category won't affect the data retrieval process, it only affects the data applying process.",
                    )}
                  >
                    <Button
                      color={
                        e.status == EnhancementRecordStatus.ContextApplied ||
                        e.status == EnhancementRecordStatus.ContextCreated
                          ? "warning"
                          : "primary"
                      }
                      isLoading={enhancing}
                      size={"sm"}
                      variant={"light"}
                      onClick={() => {
                        setEnhancing(true);
                        BApi.resource
                          .enhanceResourceByEnhancer(resourceId, e.enhancer.id)
                          .then(() => {
                            loadEnhancements();
                            setEnhancing(false);
                          });
                      }}
                    >
                      <SyncOutlined className={"text-base"} />
                      {t<string>(
                        e.status == EnhancementRecordStatus.ContextApplied
                          ? "Re-enhance now"
                          : "Enhance now",
                      )}
                    </Button>
                  </Tooltip>
                  {(e.status == EnhancementRecordStatus.ContextApplied ||
                    e.status == EnhancementRecordStatus.ContextCreated) && (
                    <Tooltip
                      color={"secondary"}
                      content={t<string>(
                        "Apply the data to property values of the resource.",
                      )}
                    >
                      <Button
                        color={"primary"}
                        isLoading={applyingContext}
                        size={"sm"}
                        variant={"light"}
                        onClick={() => {
                          setApplyingContext(true);
                          BApi.resource
                            .applyEnhancementContextDataForResourceByEnhancer(
                              resourceId,
                              e.enhancer.id,
                            )
                            .then(() => {
                              loadEnhancements();
                              setApplyingContext(false);
                            });
                        }}
                      >
                        <ApiOutlined className={"text-base"} />
                        {t<string>("Apply data")}
                      </Button>
                    </Tooltip>
                  )}
                </div>
                <div className={"flex items-center gap-2"}>
                  <Button
                    color={"primary"}
                    size={"sm"}
                    variant={"light"}
                    onClick={() => {
                      CategoryEnhancerOptionsDialog.show({
                        categoryId: resource.categoryId,
                        enhancer: e.enhancer,
                      });
                    }}
                  >
                    {t<string>("Check configuration")}
                  </Button>
                </div>
              </div>
              <div className={"flex flex-col gap-y-2 min-w-0"}>
                <div>
                  {targets.every((x) => !x.enhancement) && (
                    <Chip
                      className={"opacity-60"}
                      radius={"sm"}
                      size={"sm"}
                      variant={"light"}
                    >
                      <ExclamationCircleOutlined className={"text-sm mr-1"} />
                      {t<string>(
                        "No data retrieved for fixed targets, please check configuration if necessary",
                      )}
                    </Chip>
                  )}
                  <Table isStriped className={"break-all"}>
                    <TableHeader>
                      <TableColumn>{t<string>("Target")}</TableColumn>
                      <TableColumn>{t<string>("Raw data")}</TableColumn>
                      <TableColumn>
                        {t<string>("Generated custom property value")}
                      </TableColumn>
                    </TableHeader>
                    <TableBody>
                      {targets.map((e) => {
                        return (
                          <TableRow>
                            <TableCell>{e.targetName}</TableCell>
                            <TableCell>
                              {JSON.stringify(e.enhancement?.value)}
                            </TableCell>
                            <TableCell>
                              {renderConvertedValue(e.enhancement)}
                            </TableCell>
                          </TableRow>
                        );
                      })}
                    </TableBody>
                  </Table>
                </div>
                {e.dynamicTargets?.map((dt) => {
                  return (
                    <div>
                      {(!dt.enhancements || dt.enhancements.length == 0) && (
                        <Chip
                          className={"opacity-60"}
                          radius={"sm"}
                          size={"sm"}
                          variant={"light"}
                        >
                          <ExclamationCircleOutlined
                            className={"text-sm mr-1"}
                          />
                          {t<string>(
                            "No data retrieved for dynamic targets, please check configuration if necessary",
                          )}
                        </Chip>
                      )}
                      <Table isStriped className={"break-all"}>
                        <TableHeader>
                          <TableColumn>
                            {dt.targetName}({dt.enhancements?.length ?? 0})
                          </TableColumn>
                          <TableColumn>{t<string>("Raw data")}</TableColumn>
                          <TableColumn>
                            {t<string>("Generated custom property value")}
                          </TableColumn>
                        </TableHeader>
                        <TableBody>
                          {dt.enhancements?.map((e) => {
                            return (
                              <TableRow>
                                <TableCell>{e.dynamicTarget}</TableCell>
                                <TableCell>{JSON.stringify(e.value)}</TableCell>
                                <TableCell>{renderConvertedValue(e)}</TableCell>
                              </TableRow>
                            );
                          })}
                        </TableBody>
                      </Table>
                    </div>
                  );
                })}
              </div>
            </Tab>
          );
        })}
      </Tabs>
    </Modal>
  );
}

ResourceEnhancementsDialog.show = (props: Props) =>
  createPortalOfComponent(ResourceEnhancementsDialog, props);

export default ResourceEnhancementsDialog;
