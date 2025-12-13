"use client";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  PlusOutlined,
  DeleteOutlined,
  LinkOutlined,
} from "@ant-design/icons";
import BApi from "@/sdk/BApi";
import type {
  BakabaseAbstractionsModelsDomainMediaLibraryResourceMapping,
  BakabaseAbstractionsModelsDomainMediaLibraryV2,
} from "@/sdk/Api";
import {
  Button,
  Chip,
  Popover,
  Spinner,
  Tooltip,
  Listbox,
  ListboxItem,
  toast,
} from "@/components/bakaui";

interface Props {
  resourceId: number;
  onMappingsChange?: () => void;
  /** Compact mode: only show chips without label */
  compact?: boolean;
}

interface MappingWithLibrary extends BakabaseAbstractionsModelsDomainMediaLibraryResourceMapping {
  mediaLibrary?: BakabaseAbstractionsModelsDomainMediaLibraryV2;
}

const MediaLibraryMappings: React.FC<Props> = ({ resourceId, onMappingsChange, compact = false }) => {
  const { t } = useTranslation();
  const [loading, setLoading] = useState(true);
  const [mappings, setMappings] = useState<MappingWithLibrary[]>([]);
  const [allLibraries, setAllLibraries] = useState<BakabaseAbstractionsModelsDomainMediaLibraryV2[]>([]);
  const [addPopoverOpen, setAddPopoverOpen] = useState(false);

  const loadData = useCallback(async () => {
    setLoading(true);
    try {
      const [mappingsRsp, librariesRsp] = await Promise.all([
        BApi.mediaLibraryResourceMapping.getMappingsByResourceId(resourceId),
        BApi.mediaLibraryV2.getAllMediaLibraryV2(),
      ]);

      const libs = librariesRsp.data || [];
      setAllLibraries(libs);

      const mappingsData = mappingsRsp.data || [];
      const mappingsWithLib: MappingWithLibrary[] = mappingsData.map((m) => ({
        ...m,
        mediaLibrary: libs.find((lib) => lib.id === m.mediaLibraryId),
      }));
      setMappings(mappingsWithLib);
    } catch (error) {
      console.error("Failed to load mappings:", error);
    } finally {
      setLoading(false);
    }
  }, [resourceId]);

  useEffect(() => {
    loadData();
  }, [loadData]);

  const handleAddMapping = useCallback(
    async (mediaLibraryId: number) => {
      try {
        await BApi.mediaLibraryResourceMapping.addMediaLibraryResourceMapping({
          id: 0,
          mediaLibraryId,
          resourceId,
          createDt: new Date().toISOString(),
        });
        toast.success(t("Media library added"));
        await loadData();
        onMappingsChange?.();
        setAddPopoverOpen(false);
      } catch (error) {
        console.error("Failed to add mapping:", error);
        toast.danger(t("Failed to add media library"));
      }
    },
    [resourceId, t, loadData, onMappingsChange]
  );

  const handleRemoveMapping = useCallback(
    async (mappingId: number) => {
      try {
        await BApi.mediaLibraryResourceMapping.deleteMediaLibraryResourceMapping(
          mappingId,
        );
        toast.success(t("Media library removed"));
        await loadData();
        onMappingsChange?.();
      } catch (error) {
        console.error("Failed to remove mapping:", error);
        toast.danger(t("Failed to remove media library"));
      }
    },
    [t, loadData, onMappingsChange]
  );

  // Get libraries not yet assigned to this resource
  const availableLibraries = allLibraries.filter(
    (lib) => !mappings.some((m) => m.mediaLibraryId === lib.id)
  );

  if (loading) {
    return (
      <div className="flex items-center gap-2">
        {!compact && (
          <div className="flex items-center gap-1 text-sm text-default-500 shrink-0">
            <LinkOutlined className="text-base" />
            <span>{t("Media Libraries")}</span>
          </div>
        )}
        <Spinner size="sm" />
      </div>
    );
  }

  // In compact mode with no mappings, don't render anything
  if (compact && mappings.length === 0) {
    return null;
  }

  return (
    <div className="flex items-start gap-2">
      {!compact && (
        <div className="flex items-center gap-1 text-sm text-default-500 shrink-0 pt-0.5">
          <LinkOutlined className="text-base" />
          <span>{t("Media Libraries")}</span>
        </div>
      )}
      <div className="flex flex-wrap items-center gap-1 flex-1">
        {mappings.length === 0 ? (
          <span className="text-sm text-default-400">
            {t("None")}
          </span>
        ) : (
          mappings.map((mapping) => (
            <Chip
              key={mapping.id}
              size="sm"
              variant="flat"
              style={{
                backgroundColor: mapping.mediaLibrary?.color
                  ? `${mapping.mediaLibrary.color}20`
                  : undefined,
                color: mapping.mediaLibrary?.color,
              }}
              endContent={
                <Tooltip content={t("Remove from this media library")}>
                  <Button
                    className=""
                    isIconOnly
                    size="sm"
                    variant="light"
                    color="danger"
                    onPress={(e) => {
                      handleRemoveMapping(mapping.id);
                    }}
                  >
                    <DeleteOutlined className="text-base" />
                  </Button>
                </Tooltip>
              }
            >
              <span>{mapping.mediaLibrary?.name || t("Unknown")}</span>
            </Chip>
          ))
        )}
        <Popover
          placement="bottom-end"
          trigger={
            <Button
              isIconOnly
              isDisabled={availableLibraries.length === 0}
              size="sm"
              variant="light"
              color="primary"
              className="min-w-6 w-6 h-6"
            >
              <PlusOutlined className="text-base" />
            </Button>
          }
          visible={addPopoverOpen}
          onOpenChange={setAddPopoverOpen}
        >
          <div className="max-h-60 overflow-y-auto min-w-[200px]">
            <Listbox
              aria-label={t("Select media library")}
              onAction={(key) => {
                const id = parseInt(key as string, 10);
                handleAddMapping(id);
              }}
            >
              {availableLibraries.map((lib) => (
                <ListboxItem key={lib.id} textValue={lib.name}>
                  <div className="flex items-center gap-2">
                    <span
                      className="w-2 h-2 rounded-full"
                      style={{ backgroundColor: lib.color || "#888" }}
                    />
                    <span>{lib.name}</span>
                  </div>
                </ListboxItem>
              ))}
            </Listbox>
          </div>
        </Popover>
      </div>
    </div>
  );
};

MediaLibraryMappings.displayName = "MediaLibraryMappings";

export default MediaLibraryMappings;
