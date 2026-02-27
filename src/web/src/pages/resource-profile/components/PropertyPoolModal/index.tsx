"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { IProperty } from "@/components/Property/models";
import type { EnhancerDescriptor } from "@/components/EnhancerSelectorV2/models";
import type {
  BakabaseAbstractionsModelsDomainEnhancerFullOptions,
  BakabaseAbstractionsModelsDomainResourceProfilePropertyOptions,
  BakabaseAbstractionsModelsDomainPropertyKeyWithScopePriority,
} from "@/sdk/Api";

import { useState, useMemo } from "react";
import { useTranslation } from "react-i18next";
import { PlusOutlined, DeleteOutlined } from "@ant-design/icons";

import ScopePriorityEditor from "../ScopePriorityEditor";

import {
  Button,
  Chip,
  Modal,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
  Tooltip,
} from "@/components/bakaui";
import { PropertyPool, PropertyValueScope } from "@/sdk/constants";
import { PropertyLabel } from "@/components/Property";
import PropertySelector from "@/components/PropertySelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { enhancerIdToScope } from "../EnhancementConfigPanel/utils";
import type { EnhancerId } from "@/sdk/constants";

type PropertyRef = BakabaseAbstractionsModelsDomainPropertyKeyWithScopePriority;

type Props = {
  propertyOptions?: BakabaseAbstractionsModelsDomainResourceProfilePropertyOptions;
  allProperties: IProperty[];
  enhancerOptions: BakabaseAbstractionsModelsDomainEnhancerFullOptions[];
  enhancerDescriptors: EnhancerDescriptor[];
  onSubmit?: (options: BakabaseAbstractionsModelsDomainResourceProfilePropertyOptions | undefined) => any;
} & DestroyableProps;

const PropertyPoolModal = ({
  propertyOptions,
  allProperties,
  enhancerOptions,
  enhancerDescriptors,
  onSubmit,
  onDestroyed,
}: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [propertyRefs, setPropertyRefs] = useState<PropertyRef[]>(
    propertyOptions?.properties ?? []
  );

  // Compute relevant scopes for a property based on enhancer configurations
  const getAvailableScopes = (pool: PropertyPool, id: number): PropertyValueScope[] => {
    const scopes: PropertyValueScope[] = [
      PropertyValueScope.Manual,
      PropertyValueScope.Synchronization,
    ];

    for (const opt of enhancerOptions) {
      if (!opt.targetOptions) continue;
      const hasTarget = opt.targetOptions.some(
        (to) => to.propertyPool === pool && to.propertyId === id
      );
      if (hasTarget && opt.enhancerId != null) {
        const scope = enhancerIdToScope(opt.enhancerId as EnhancerId);
        if (scope && !scopes.includes(scope)) {
          scopes.push(scope);
        }
      }
    }

    return scopes;
  };

  const findProperty = (pool: PropertyPool, id: number): IProperty | undefined => {
    return allProperties.find((p) => p.pool === pool && p.id === id);
  };

  const handleAddProperties = () => {
    createPortal(PropertySelector, {
      v2: true,
      pool: PropertyPool.Reserved | PropertyPool.Custom,
      multiple: true,
      selection: propertyRefs.map((ref) => ({ pool: ref.pool!, id: ref.id! })),
      onSubmit: async (selectedProperties: IProperty[]) => {
        // Merge: keep existing refs with their scopePriority, add new ones
        const newRefs: PropertyRef[] = selectedProperties.map((p) => {
          const existing = propertyRefs.find(
            (r) => r.pool === p.pool && r.id === p.id
          );
          return existing ?? { pool: p.pool, id: p.id };
        });
        setPropertyRefs(newRefs);
      },
    });
  };

  const handleRemoveProperty = (pool: PropertyPool, id: number) => {
    setPropertyRefs((prev) => prev.filter((r) => !(r.pool === pool && r.id === id)));
  };

  const handleScopePriorityChange = (
    pool: PropertyPool,
    id: number,
    scopePriority: PropertyValueScope[] | null
  ) => {
    setPropertyRefs((prev) =>
      prev.map((r) => {
        if (r.pool === pool && r.id === id) {
          return { ...r, scopePriority: scopePriority ?? undefined };
        }
        return r;
      })
    );
  };

  const handleSubmit = () => {
    const result = propertyRefs.length > 0 ? { properties: propertyRefs } : undefined;
    onSubmit?.(result);
  };

  return (
    <Modal
      defaultVisible
      classNames={{ base: "max-w-[80vw]" }}
      size="3xl"
      title={t<string>("resourceProfile.propertyPool.title")}
      onDestroyed={onDestroyed}
      onOk={handleSubmit}
    >
      <div className="flex flex-col gap-3">
        <div className="flex items-center justify-between">
          <div className="text-sm text-default-500">
            {t("resourceProfile.propertyPool.description")}
          </div>
          <Button
            size="sm"
            color="primary"
            startContent={<PlusOutlined />}
            onPress={handleAddProperties}
          >
            {t<string>("resourceProfile.propertyPool.addProperty")}
          </Button>
        </div>

        {propertyRefs.length === 0 ? (
          <div className="py-8 text-center text-default-400">
            {t("resourceProfile.propertyPool.empty")}
          </div>
        ) : (
          <Table
            aria-label="Property Pool Table"
            removeWrapper
          >
            <TableHeader>
              <TableColumn>{t("resourceProfile.propertyPool.columnProperty")}</TableColumn>
              <TableColumn>{t("resourceProfile.propertyPool.columnType")}</TableColumn>
              <TableColumn width={300}>
                {t("resourceProfile.propertyPool.columnScopePriority")}
              </TableColumn>
              <TableColumn width={60}>{""}</TableColumn>
            </TableHeader>
            <TableBody>
              {propertyRefs.map((ref) => {
                const property = findProperty(ref.pool!, ref.id!);
                const availableScopes = getAvailableScopes(ref.pool!, ref.id!);

                return (
                  <TableRow key={`${ref.pool}-${ref.id}`}>
                    <TableCell>
                      {property ? (
                        <PropertyLabel
                          property={{
                            name: property.name!,
                            type: property.type!,
                            pool: property.pool!,
                          }}
                        />
                      ) : (
                        <span className="text-default-400">
                          {t("resourceProfile.propertyPool.unknownProperty")}
                        </span>
                      )}
                    </TableCell>
                    <TableCell>
                      {property && (
                        <Chip size="sm" variant="flat">
                          {property.type != null
                            ? t(`PropertyType.${property.type}`)
                            : "-"}
                        </Chip>
                      )}
                    </TableCell>
                    <TableCell>
                      <ScopePriorityEditor
                        value={ref.scopePriority ?? null}
                        availableScopes={availableScopes}
                        onChange={(newPriority) =>
                          handleScopePriorityChange(ref.pool!, ref.id!, newPriority)
                        }
                      />
                    </TableCell>
                    <TableCell>
                      <Tooltip content={t("common.action.delete")}>
                        <Button
                          isIconOnly
                          size="sm"
                          variant="light"
                          color="danger"
                          onPress={() => handleRemoveProperty(ref.pool!, ref.id!)}
                        >
                          <DeleteOutlined className="text-xs" />
                        </Button>
                      </Tooltip>
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        )}
      </div>
    </Modal>
  );
};

PropertyPoolModal.displayName = "PropertyPoolModal";

export default PropertyPoolModal;
