"use client";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Divider, Input, Spinner } from "@heroui/react";
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

  const [mappings, setMappings] = useState<SourceMetadataMapping[]>([]);
  const [properties, setProperties] = useState<Record<string, IProperty>>({});
  const [loading, setLoading] = useState(true);
  const [applying, setApplying] = useState(false);
  const [newFieldName, setNewFieldName] = useState("");

  const saveTimeoutRef = useRef<any>();

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await fetchApi<any>(`/source/${source}/metadata-mapping`);
      setMappings(res?.data ?? []);
    } finally {
      setLoading(false);
    }
  }, [source]);

  useEffect(() => {
    load();
  }, [load]);

  const saveMappings = useCallback(
    (updated: SourceMetadataMapping[]) => {
      // Debounce saves
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

  const handleAddMapping = (property: IProperty) => {
    if (!newFieldName.trim()) return;

    const fieldName = newFieldName.trim();
    // Don't add duplicate field names
    if (mappings.some((m) => m.metadataField === fieldName)) {
      return;
    }

    const updated = [
      ...mappings,
      {
        source,
        metadataField: fieldName,
        targetPool: property.pool!,
        targetPropertyId: property.id!,
      },
    ];
    setMappings(updated);
    setProperties((prev) => ({ ...prev, [fieldName]: property }));
    setNewFieldName("");
    saveMappings(updated);
  };

  const handleChangeProperty = (fieldName: string, property: IProperty) => {
    const updated = mappings.map((m) =>
      m.metadataField === fieldName
        ? { ...m, targetPool: property.pool!, targetPropertyId: property.id! }
        : m,
    );
    setMappings(updated);
    setProperties((prev) => ({ ...prev, [fieldName]: property }));
    saveMappings(updated);
  };

  const handleRemoveMapping = (fieldName: string) => {
    const updated = mappings.filter((m) => m.metadataField !== fieldName);
    setMappings(updated);
    setProperties((prev) => {
      const next = { ...prev };
      delete next[fieldName];
      return next;
    });
    saveMappings(updated);
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

  if (loading) {
    return (
      <div className="flex justify-center py-4">
        <Spinner size="sm" />
      </div>
    );
  }

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

      {/* Existing mappings */}
      <div className="space-y-2">
        {mappings.map((mapping) => {
          const cachedProp = properties[mapping.metadataField];

          return (
            <div
              key={mapping.metadataField}
              className="flex items-center gap-2 border-small border-default-200 rounded-lg px-3 py-2"
            >
              <code className="text-xs bg-default-100 px-2 py-1 rounded min-w-[100px]">
                {mapping.metadataField}
              </code>
              <span className="text-default-400">&rarr;</span>
              <Button
                className="flex-1"
                size="sm"
                variant="light"
                onPress={() =>
                  openPropertySelector((p) =>
                    handleChangeProperty(mapping.metadataField, p),
                  )
                }
              >
                {cachedProp ? (
                  <BriefProperty property={cachedProp} />
                ) : (
                  `${mapping.targetPool}:${mapping.targetPropertyId}`
                )}
              </Button>
              <Button
                color="danger"
                isIconOnly
                size="sm"
                variant="light"
                onPress={() => handleRemoveMapping(mapping.metadataField)}
              >
                <AiOutlineDelete />
              </Button>
            </div>
          );
        })}
      </div>

      {/* Add new mapping */}
      <div className="flex items-center gap-2">
        <Input
          className="flex-1"
          placeholder={t(
            "resourceSource.metadataMapping.fieldPlaceholder",
          )}
          size="sm"
          value={newFieldName}
          onValueChange={setNewFieldName}
          onKeyDown={(e) => {
            if (e.key === "Enter" && newFieldName.trim()) {
              openPropertySelector((p) => handleAddMapping(p));
            }
          }}
        />
        <Button
          color="primary"
          isDisabled={!newFieldName.trim()}
          size="sm"
          startContent={<AiOutlinePlus />}
          variant="flat"
          onPress={() => openPropertySelector((p) => handleAddMapping(p))}
        >
          {t("resourceSource.metadataMapping.add")}
        </Button>
      </div>
    </div>
  );
}
