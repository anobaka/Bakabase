"use client";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  WarningOutlined,
  DeleteOutlined,
  MergeCellsOutlined,
  CheckOutlined,
} from "@ant-design/icons";
import BApi from "@/sdk/BApi";
import type { Resource as ResourceModel, Property } from "@/core/models/Resource";
import type { DestroyableProps } from "@/components/bakaui/types";
import {
  Button,
  Chip,
  Modal,
  Radio,
  RadioGroup,
  Spinner,
  Tooltip,
  toast,
} from "@/components/bakaui";
import {
  PropertyPool,
  PropertyValueScopeLabel,
  ResourceAdditionalItem,
  ResourceSourceLabel,
} from "@/sdk/constants";
import type { PropertyValueScope, ResourceSource } from "@/sdk/constants";
import { convertFromApiValue } from "@/components/StandardValue/helpers";
import ResourceCover from "@/components/Resource/components/ResourceCover";

type ResourceAction = "merge" | "delete";

type Props = {
  resourceId: number;
  onResolved?: () => void;
} & DestroyableProps;

const ConflictResolutionModal = ({ resourceId, onResolved, onDestroyed }: Props) => {
  const { t } = useTranslation();
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [conflicts, setConflicts] = useState<ResourceModel[]>([]);
  const [currentResource, setCurrentResource] = useState<ResourceModel | undefined>();
  // action per conflict resource: merge into current, or delete
  const [actions, setActions] = useState<Record<number, ResourceAction>>({});

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      // Load current resource
      // @ts-ignore
      const currentRsp = await BApi.resource.getResourcesByKeys({
        ids: [resourceId],
        additionalItems: ResourceAdditionalItem.All,
      });
      const currentRes = (currentRsp.data || [])?.[0];
      if (currentRes?.properties) {
        Object.values(currentRes.properties).forEach((a: any) => {
          Object.values(a).forEach((b: any) => {
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
      setCurrentResource(currentRes as any);

      // Load conflicting resources
      // @ts-ignore
      const conflictsRsp = await BApi.resource.getResourceConflicts(resourceId, {
        additionalItems: ResourceAdditionalItem.All,
      });
      const conflictData = (conflictsRsp.data || []) as ResourceModel[];

      for (const res of conflictData) {
        if (res.properties) {
          Object.values(res.properties).forEach((a: any) => {
            Object.values(a).forEach((b: any) => {
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
      }

      setConflicts(conflictData);
      // Default action: merge
      const defaultActions: Record<number, ResourceAction> = {};
      for (const c of conflictData) {
        defaultActions[c.id] = "merge";
      }
      setActions(defaultActions);
    } catch (error) {
      console.error("Failed to load conflict data:", error);
    } finally {
      setLoading(false);
    }
  }, [resourceId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleSubmit = useCallback(async () => {
    if (!currentResource) return;
    setSubmitting(true);
    try {
      const sourceResourceIds = Object.entries(actions)
        .filter(([, action]) => action === "merge")
        .map(([id]) => parseInt(id, 10));
      const deleteResourceIds = Object.entries(actions)
        .filter(([, action]) => action === "delete")
        .map(([id]) => parseInt(id, 10));

      if (sourceResourceIds.length === 0 && deleteResourceIds.length === 0) {
        return;
      }

      await BApi.resource.mergeResources({
        targetResourceId: currentResource.id,
        sourceResourceIds,
        deleteResourceIds,
      });
      toast.success(t("resource.conflict.mergeSuccess"));
      onResolved?.();
    } catch (error) {
      console.error("Failed to merge resources:", error);
      toast.danger(t("resource.conflict.mergeFailed"));
    } finally {
      setSubmitting(false);
    }
  }, [currentResource, actions, onResolved, t]);

  // Collect all property keys across all resources (current + conflicts)
  const allResources = currentResource ? [currentResource, ...conflicts] : conflicts;

  // Gather all unique property identifiers: { pool, id, name }
  type PropertyKey = { pool: PropertyPool; id: number; name: string; property: Property };
  const propertyKeys: PropertyKey[] = [];
  const seenKeys = new Set<string>();

  for (const res of allResources) {
    if (!res.properties) continue;
    for (const [poolStr, props] of Object.entries(res.properties)) {
      const pool = parseInt(poolStr, 10) as PropertyPool;
      for (const [idStr, prop] of Object.entries(props as Record<number, Property>)) {
        const id = parseInt(idStr, 10);
        const key = `${pool}:${id}`;
        if (!seenKeys.has(key)) {
          seenKeys.add(key);
          propertyKeys.push({ pool, id, name: prop.name || `#${id}`, property: prop });
        }
      }
    }
  }

  // Gather all unique value scopes across all resources and properties
  const allScopes = new Set<PropertyValueScope>();
  for (const res of allResources) {
    if (!res.properties) continue;
    for (const props of Object.values(res.properties)) {
      for (const prop of Object.values(props as Record<number, Property>)) {
        if (prop.values) {
          for (const v of prop.values) {
            allScopes.add(v.scope);
          }
        }
      }
    }
  }
  const sortedScopes = Array.from(allScopes).sort((a, b) => a - b);

  const renderValuePreview = (value: any): string => {
    if (value === null || value === undefined) return "-";
    if (typeof value === "string") return value || "-";
    if (typeof value === "number") return String(value);
    if (typeof value === "boolean") return value ? "true" : "false";
    if (Array.isArray(value)) {
      if (value.length === 0) return "-";
      return value.map((v) => (typeof v === "object" ? JSON.stringify(v) : String(v))).join(", ");
    }
    if (typeof value === "object") return JSON.stringify(value);
    return String(value);
  };

  return (
    <Modal
      defaultVisible
      size="full"
      title={
        <div className="flex items-center gap-2">
          <WarningOutlined className="text-warning" />
          {t("resource.conflict.title")}
        </div>
      }
      footer={
        <div className="flex items-center w-full gap-2">
          <div className="flex-1" />
          <Button
            color="primary"
            isLoading={submitting}
            isDisabled={conflicts.length === 0}
            onPress={handleSubmit}
          >
            <MergeCellsOutlined className="text-base" />
            {t("resource.conflict.apply")}
          </Button>
        </div>
      }
      onDestroyed={onDestroyed}
    >
      {loading ? (
        <div className="flex items-center justify-center min-h-[200px]">
          <Spinner label={t<string>("resource.conflict.loading")} />
        </div>
      ) : conflicts.length === 0 ? (
        <div className="flex items-center justify-center min-h-[200px] text-default-400">
          {t("resource.conflict.noConflicts")}
        </div>
      ) : (
        <div className="flex flex-col gap-4">
          {/* Description */}
          <div className="text-sm text-default-500">
            {t("resource.conflict.description")}
          </div>

          {/* Resource comparison table */}
          <div className="overflow-x-auto">
            <table className="w-full text-sm border-collapse">
              <thead>
                <tr className="border-b-2 border-default-200">
                  <th className="p-2 text-left text-default-500 sticky left-0 bg-content1 min-w-[120px]">
                    {t("resource.conflict.property")}
                  </th>
                  <th className="p-2 text-left text-default-500 min-w-[80px]">
                    {t("resource.conflict.scope")}
                  </th>
                  {allResources.map((res) => (
                    <th
                      key={res.id}
                      className="p-2 text-left min-w-[200px]"
                    >
                      <div className="flex flex-col gap-1">
                        <div className="flex items-center gap-2">
                          <div className="w-12 h-12 rounded overflow-hidden border flex-shrink-0">
                            <ResourceCover resource={res} showBiggerOnHover={false} />
                          </div>
                          <div className="flex flex-col gap-0.5">
                            <span className="font-medium truncate max-w-[180px]">
                              {res.displayName || res.fileName || `#${res.id}`}
                            </span>
                            {res.id === resourceId && (
                              <Chip size="sm" color="primary" variant="flat">
                                {t("resource.conflict.current")}
                              </Chip>
                            )}
                          </div>
                        </div>
                        {res.id !== resourceId && (
                          <div className="flex items-center gap-2 mt-1">
                            <RadioGroup
                              orientation="horizontal"
                              size="sm"
                              value={actions[res.id] || "merge"}
                              onValueChange={(val) => {
                                setActions((prev) => ({
                                  ...prev,
                                  [res.id]: val as ResourceAction,
                                }));
                              }}
                            >
                              <Radio value="merge">
                                <span className="text-xs flex items-center gap-1">
                                  <MergeCellsOutlined />
                                  {t("resource.conflict.actionMerge")}
                                </span>
                              </Radio>
                              <Radio value="delete">
                                <span className="text-xs flex items-center gap-1 text-danger">
                                  <DeleteOutlined />
                                  {t("resource.conflict.actionDelete")}
                                </span>
                              </Radio>
                            </RadioGroup>
                          </div>
                        )}
                      </div>
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {/* Source Links row */}
                <tr className="border-b border-default-100 bg-default-50">
                  <td className="p-2 font-medium sticky left-0 bg-default-50">
                    {t("resource.conflict.sourceLinks")}
                  </td>
                  <td className="p-2 text-default-400">-</td>
                  {allResources.map((res) => (
                    <td key={res.id} className="p-2">
                      <div className="flex flex-wrap gap-1">
                        {(res as any).sourceLinks?.map((link: any) => (
                          <Chip key={link.id} size="sm" variant="flat" color="secondary">
                            {ResourceSourceLabel[link.source as ResourceSource]}: {link.sourceKey}
                          </Chip>
                        )) || <span className="text-default-400">-</span>}
                      </div>
                    </td>
                  ))}
                </tr>

                {/* Media Libraries row */}
                <tr className="border-b border-default-100">
                  <td className="p-2 font-medium sticky left-0 bg-content1">
                    {t("resource.conflict.mediaLibraries")}
                  </td>
                  <td className="p-2 text-default-400">-</td>
                  {allResources.map((res) => (
                    <td key={res.id} className="p-2">
                      <div className="flex flex-wrap gap-1">
                        {res.mediaLibraries?.map((ml) => (
                          <Chip
                            key={ml.id}
                            size="sm"
                            variant="flat"
                            style={{
                              backgroundColor: ml.color ? `${ml.color}20` : undefined,
                              color: ml.color,
                            }}
                          >
                            {ml.name}
                          </Chip>
                        )) || <span className="text-default-400">-</span>}
                      </div>
                    </td>
                  ))}
                </tr>

                {/* Path row */}
                <tr className="border-b border-default-100 bg-default-50">
                  <td className="p-2 font-medium sticky left-0 bg-default-50">
                    {t("resource.conflict.path")}
                  </td>
                  <td className="p-2 text-default-400">-</td>
                  {allResources.map((res) => (
                    <td key={res.id} className="p-2">
                      <span className="text-xs break-all">{res.path || "-"}</span>
                    </td>
                  ))}
                </tr>

                {/* Property rows - one per property per scope */}
                {propertyKeys.map((pk) => (
                  <React.Fragment key={`${pk.pool}:${pk.id}`}>
                    {sortedScopes.map((scope, scopeIdx) => {
                      // Check if any resource has a value for this property+scope
                      const hasValue = allResources.some((res) => {
                        const prop = res.properties?.[pk.pool]?.[pk.id];
                        return prop?.values?.some((v) => v.scope === scope);
                      });
                      if (!hasValue) return null;

                      return (
                        <tr
                          key={`${pk.pool}:${pk.id}:${scope}`}
                          className={`border-b border-default-100 ${scopeIdx % 2 === 0 ? "bg-default-50" : ""}`}
                        >
                          <td className={`p-2 sticky left-0 ${scopeIdx % 2 === 0 ? "bg-default-50" : "bg-content1"}`}>
                            {scopeIdx === 0 ? (
                              <span className="font-medium">{pk.name}</span>
                            ) : (
                              <span className="text-default-300">{pk.name}</span>
                            )}
                          </td>
                          <td className="p-2">
                            <Chip size="sm" variant="flat" color="default">
                              {PropertyValueScopeLabel[scope] || `Scope ${scope}`}
                            </Chip>
                          </td>
                          {allResources.map((res) => {
                            const prop = res.properties?.[pk.pool]?.[pk.id];
                            const val = prop?.values?.find((v) => v.scope === scope);
                            const displayVal = val?.aliasAppliedBizValue ?? val?.bizValue;

                            return (
                              <td key={res.id} className="p-2">
                                <span className="text-sm break-all">
                                  {renderValuePreview(displayVal)}
                                </span>
                              </td>
                            );
                          })}
                        </tr>
                      );
                    })}
                  </React.Fragment>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}
    </Modal>
  );
};

ConflictResolutionModal.displayName = "ConflictResolutionModal";

export default ConflictResolutionModal;
