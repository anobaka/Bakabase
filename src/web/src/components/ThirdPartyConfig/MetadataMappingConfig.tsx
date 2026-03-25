"use client";

import { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Chip, Divider, Spinner } from "@heroui/react";
import { AiOutlineSync } from "react-icons/ai";

import { toast } from "@/components/bakaui";
import { ResourceSource, type PropertyPool } from "@/sdk/constants";
import PropertySelector from "@/components/PropertySelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BriefProperty from "@/components/Chips/Property/BriefProperty";
import type { IProperty } from "@/components/Property/models";
import envConfig from "@/config/env";

type SourceMetadataMapping = {
  id?: number;
  source: ResourceSource;
  metadataField: string;
  targetPool: PropertyPool;
  targetPropertyId: number;
};

type MetadataFieldInfo = {
  name: string;
  valueType: number;
};

interface Props {
  source: ResourceSource;
}

async function fetchApi<T>(path: string, options?: RequestInit): Promise<T> {
  const endpoint = envConfig.apiEndpoint || "";
  const res = await fetch(`${endpoint}${path}`, {
    headers: { "Content-Type": "application/json" },
    ...options,
  });
  const json = await res.json();
  return json;
}

export default function MetadataMappingConfig({ source }: Props) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [fields, setFields] = useState<MetadataFieldInfo[]>([]);
  const [mappings, setMappings] = useState<SourceMetadataMapping[]>([]);
  const [properties, setProperties] = useState<Record<string, IProperty>>({});
  const [loading, setLoading] = useState(true);
  const [applying, setApplying] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [fieldsRes, mappingsRes] = await Promise.all([
        fetchApi<any>(`/source/${source}/metadata-mapping/available-fields`),
        fetchApi<any>(`/source/${source}/metadata-mapping`),
      ]);

      const rawFields = fieldsRes?.data ?? [];
      // Handle both old (string[]) and new (SourceMetadataFieldInfo[]) formats
      const parsedFields: MetadataFieldInfo[] = rawFields.map((f: any) =>
        typeof f === "string" ? { name: f, valueType: 0 } : f,
      );
      setFields(parsedFields);
      setMappings(mappingsRes?.data ?? []);
    } finally {
      setLoading(false);
    }
  }, [source]);

  useEffect(() => {
    load();
  }, [load]);

  const getMappingForField = (fieldName: string) =>
    mappings.find((m) => m.metadataField === fieldName);

  const handlePropertyChange = async (
    fieldName: string,
    property?: IProperty,
  ) => {
    let updated: SourceMetadataMapping[];
    if (property) {
      const existing = getMappingForField(fieldName);
      if (existing) {
        updated = mappings.map((m) =>
          m.metadataField === fieldName
            ? {
                ...m,
                targetPool: property.pool!,
                targetPropertyId: property.id!,
              }
            : m,
        );
      } else {
        updated = [
          ...mappings,
          {
            source,
            metadataField: fieldName,
            targetPool: property.pool!,
            targetPropertyId: property.id!,
          },
        ];
      }
    } else {
      updated = mappings.filter((m) => m.metadataField !== fieldName);
    }

    setMappings(updated);
    setProperties((prev) =>
      property
        ? { ...prev, [fieldName]: property }
        : (() => {
            const next = { ...prev };
            delete next[fieldName];
            return next;
          })(),
    );

    // Save to backend
    await fetchApi(`/source/${source}/metadata-mapping`, {
      method: "PUT",
      body: JSON.stringify(updated),
    });
  };

  const handleReapply = async () => {
    if (!confirm(t("resourceSource.metadataMapping.reapplyConfirm"))) return;
    setApplying(true);
    try {
      await fetchApi(`/source/${source}/metadata-mapping/apply-all`, {
        method: "POST",
      });
      toast.success(t("resourceSource.metadataMapping.reapplyStarted"));
    } finally {
      setApplying(false);
    }
  };

  if (loading) {
    return (
      <div className="flex justify-center py-4">
        <Spinner size="sm" />
      </div>
    );
  }

  if (fields.length === 0) return null;

  return (
    <div className="space-y-3">
      <Divider />
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium">
          {t("resourceSource.metadataMapping.title")}
        </span>
        <Button
          color="primary"
          isLoading={applying}
          size="sm"
          startContent={<AiOutlineSync />}
          variant="flat"
          onPress={handleReapply}
        >
          {t("resourceSource.metadataMapping.reapply")}
        </Button>
      </div>
      <p className="text-xs text-default-400">
        {t("resourceSource.metadataMapping.description")}
      </p>

      <div className="space-y-2">
        {fields.map((field) => {
          const mapping = getMappingForField(field.name);
          const cachedProp = properties[field.name];

          return (
            <div
              key={field.name}
              className="flex items-center gap-2 border-small border-default-200 rounded-lg px-3 py-2"
            >
              <Chip className="min-w-[120px]" size="sm" variant="flat">
                {field.name}
              </Chip>
              <span className="text-default-400">&rarr;</span>
              {mapping && cachedProp ? (
                <Button
                  size="sm"
                  variant="light"
                  onPress={() => {
                    createPortal(PropertySelector, {
                      v2: true,
                      pool: 6, // PropertyPool.Custom | PropertyPool.Reserved
                      multiple: false,
                      onSubmit: async (selection: IProperty[]) => {
                        handlePropertyChange(field.name, selection[0]);
                      },
                    });
                  }}
                >
                  <BriefProperty property={cachedProp} />
                </Button>
              ) : mapping ? (
                <Chip size="sm" variant="flat">
                  {`${mapping.targetPool}:${mapping.targetPropertyId}`}
                </Chip>
              ) : (
                <Button
                  color="default"
                  size="sm"
                  variant="flat"
                  onPress={() => {
                    createPortal(PropertySelector, {
                      v2: true,
                      pool: 6, // PropertyPool.Custom | PropertyPool.Reserved
                      multiple: false,
                      onSubmit: async (selection: IProperty[]) => {
                        handlePropertyChange(field.name, selection[0]);
                      },
                    });
                  }}
                >
                  {t("resourceSource.metadataMapping.notConfigured")}
                </Button>
              )}
              {mapping && (
                <Button
                  color="danger"
                  isIconOnly
                  size="sm"
                  variant="light"
                  onPress={() => handlePropertyChange(field.name)}
                >
                  &times;
                </Button>
              )}
            </div>
          );
        })}
      </div>
    </div>
  );
}
