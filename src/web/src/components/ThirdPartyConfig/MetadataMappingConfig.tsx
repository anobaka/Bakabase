"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Chip, Divider, Input, Spinner } from "@heroui/react";
import { AiOutlineDelete, AiOutlinePlus, AiOutlineSync } from "react-icons/ai";

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

type PredefinedField = {
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
  return res.json();
}

export default function MetadataMappingConfig({ source }: Props) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [predefinedFields, setPredefinedFields] = useState<PredefinedField[]>([]);
  const [mappings, setMappings] = useState<SourceMetadataMapping[]>([]);
  const [properties, setProperties] = useState<Record<string, IProperty>>({});
  const [loading, setLoading] = useState(true);
  const [applying, setApplying] = useState(false);
  const [newFieldName, setNewFieldName] = useState("");

  const saveTimeoutRef = useRef<any>();

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [fieldsRes, mappingsRes] = await Promise.all([
        fetchApi<any>(`/source/${source}/metadata-mapping/predefined-fields`),
        fetchApi<any>(`/source/${source}/metadata-mapping`),
      ]);
      setPredefinedFields(fieldsRes?.data ?? []);
      setMappings(mappingsRes?.data ?? []);
    } finally {
      setLoading(false);
    }
  }, [source]);

  useEffect(() => {
    load();
  }, [load]);

  const predefinedFieldNames = new Set(predefinedFields.map((f) => f.name));
  const customMappings = mappings.filter((m) => !predefinedFieldNames.has(m.metadataField));

  const saveMappings = useCallback(
    (updated: SourceMetadataMapping[]) => {
      clearTimeout(saveTimeoutRef.current);
      saveTimeoutRef.current = setTimeout(() => {
        fetchApi(`/source/${source}/metadata-mapping`, {
          method: "PUT",
          body: JSON.stringify(updated),
        });
      }, 300);
    },
    [source],
  );

  const getMappingForField = (fieldName: string) =>
    mappings.find((m) => m.metadataField === fieldName);

  const setPropertyForField = (fieldName: string, property: IProperty) => {
    const existing = getMappingForField(fieldName);
    let updated: SourceMetadataMapping[];
    if (existing) {
      updated = mappings.map((m) =>
        m.metadataField === fieldName
          ? { ...m, targetPool: property.pool!, targetPropertyId: property.id! }
          : m,
      );
    } else {
      updated = [
        ...mappings,
        { source, metadataField: fieldName, targetPool: property.pool!, targetPropertyId: property.id! },
      ];
    }
    setMappings(updated);
    setProperties((prev) => ({ ...prev, [fieldName]: property }));
    saveMappings(updated);
  };

  const removeMapping = (fieldName: string) => {
    const updated = mappings.filter((m) => m.metadataField !== fieldName);
    setMappings(updated);
    setProperties((prev) => { const next = { ...prev }; delete next[fieldName]; return next; });
    saveMappings(updated);
  };

  const handleReapply = async () => {
    if (!confirm(t("resourceSource.metadataMapping.reapplyConfirm"))) return;
    setApplying(true);
    try {
      await fetchApi(`/source/${source}/metadata-mapping/apply-all`, { method: "POST" });
      toast.success(t("resourceSource.metadataMapping.reapplyStarted"));
    } finally {
      setApplying(false);
    }
  };

  const openPropertySelector = (onSelect: (p: IProperty) => void) => {
    createPortal(PropertySelector, {
      v2: true,
      pool: 6, // PropertyPool.Custom | PropertyPool.Reserved
      multiple: false,
      onSubmit: async (selection: IProperty[]) => {
        if (selection[0]) onSelect(selection[0]);
      },
    });
  };

  const handleAddCustomMapping = (property: IProperty) => {
    const fieldName = newFieldName.trim();
    if (!fieldName || mappings.some((m) => m.metadataField === fieldName)) return;
    setPropertyForField(fieldName, property);
    setNewFieldName("");
  };

  if (loading) {
    return <div className="flex justify-center py-4"><Spinner size="sm" /></div>;
  }

  const renderMappingRow = (fieldName: string, isPredefined: boolean) => {
    const mapping = getMappingForField(fieldName);
    const cachedProp = properties[fieldName];

    return (
      <div
        key={fieldName}
        className="flex items-center gap-2 border-small border-default-200 rounded-lg px-3 py-2"
      >
        <code className={`text-xs px-2 py-1 rounded min-w-[100px] ${isPredefined ? "bg-primary-50 text-primary-700" : "bg-default-100"}`}>
          {fieldName}
        </code>
        <span className="text-default-400">&rarr;</span>
        <Button
          className="flex-1 justify-start"
          size="sm"
          variant={mapping ? "light" : "flat"}
          color={mapping ? "default" : "default"}
          onPress={() => openPropertySelector((p) => setPropertyForField(fieldName, p))}
        >
          {mapping && cachedProp ? (
            <BriefProperty property={cachedProp} />
          ) : mapping ? (
            `${mapping.targetPool}:${mapping.targetPropertyId}`
          ) : (
            <span className="text-default-400">{t("resourceSource.metadataMapping.notConfigured")}</span>
          )}
        </Button>
        {mapping && (
          <Button
            color="danger"
            isIconOnly
            size="sm"
            variant="light"
            onPress={() => removeMapping(fieldName)}
          >
            <AiOutlineDelete />
          </Button>
        )}
      </div>
    );
  };

  return (
    <div className="space-y-3">
      <Divider />
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium">
          {t("resourceSource.metadataMapping.title")}
        </span>
        {mappings.length > 0 && (
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
        )}
      </div>
      <p className="text-xs text-default-400">
        {t("resourceSource.metadataMapping.description")}
      </p>

      {/* Predefined fields (always shown) */}
      {predefinedFields.length > 0 && (
        <div className="space-y-2">
          {predefinedFields.map((field) => renderMappingRow(field.name, true))}
        </div>
      )}

      {/* Custom fields (user-added) */}
      {customMappings.length > 0 && (
        <div className="space-y-2 mt-2">
          <span className="text-xs text-default-500">{t("resourceSource.metadataMapping.customFields")}</span>
          {customMappings.map((m) => renderMappingRow(m.metadataField, false))}
        </div>
      )}

      {/* Add custom field */}
      <div className="flex items-center gap-2">
        <Input
          className="flex-1"
          placeholder={t("resourceSource.metadataMapping.fieldPlaceholder")}
          size="sm"
          value={newFieldName}
          onValueChange={setNewFieldName}
          onKeyDown={(e) => {
            if (e.key === "Enter" && newFieldName.trim()) {
              openPropertySelector((p) => handleAddCustomMapping(p));
            }
          }}
        />
        <Button
          color="primary"
          isDisabled={!newFieldName.trim()}
          size="sm"
          startContent={<AiOutlinePlus />}
          variant="flat"
          onPress={() => openPropertySelector((p) => handleAddCustomMapping(p))}
        >
          {t("resourceSource.metadataMapping.add")}
        </Button>
      </div>
    </div>
  );
}
