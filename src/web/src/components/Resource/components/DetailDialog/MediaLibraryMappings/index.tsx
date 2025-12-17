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
}

interface MappingWithLibrary extends BakabaseAbstractionsModelsDomainMediaLibraryResourceMapping {
  mediaLibrary?: BakabaseAbstractionsModelsDomainMediaLibraryV2;
}

const MediaLibraryMappings: React.FC<Props> = ({ resourceId, onMappingsChange }) => {
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
        await BApi.mediaLibraryResourceMapping.addMapping({
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
        toast.error(t("Failed to add media library"));
      }
    },
    [resourceId, t, loadData, onMappingsChange]
  );

  const handleRemoveMapping = useCallback(
    async (mappingId: number) => {
      try {
        await BApi.mediaLibraryResourceMapping.deleteMapping(mappingId);
        toast.success(t("Media library removed"));
        await loadData();
        onMappingsChange?.();
      } catch (error) {
        console.error("Failed to remove mapping:", error);
        toast.error(t("Failed to remove media library"));
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
      <div className="flex items-center gap-2 py-2">
        <Spinner size="sm" />
        <span className="text-sm text-default-500">{t("Loading...")}</span>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-1 text-sm font-medium">
          <LinkOutlined className="text-base" />
          {t("Media Libraries")}
        </div>
        <Popover
          isOpen={addPopoverOpen}
          placement="bottom-end"
          onOpenChange={setAddPopoverOpen}
        >
          <Popover.Trigger>
            <Button
              isIconOnly
              isDisabled={availableLibraries.length === 0}
              size="sm"
              variant="light"
              color="primary"
            >
              <PlusOutlined />
            </Button>
          </Popover.Trigger>
          <Popover.Content>
            <div className="max-h-60 overflow-y-auto min-w-[200px]">
              <Listbox
                aria-label={t("Select media library")}
                onAction={(key) => {
                  const id = parseInt(key as string, 10);
                  handleAddMapping(id);
                }}
              >
                {availableLibraries.map((lib) => (
                  <ListboxItem
                    key={lib.id}
                    textValue={lib.name}
                  >
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
          </Popover.Content>
        </Popover>
      </div>

      {mappings.length === 0 ? (
        <div className="text-sm text-default-400 py-1">
          {t("Not assigned to any media library")}
        </div>
      ) : (
        <div className="flex flex-wrap gap-1">
          {mappings.map((mapping) => (
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
                  <button
                    className="ml-1 opacity-60 hover:opacity-100 transition-opacity"
                    onClick={(e) => {
                      e.stopPropagation();
                      handleRemoveMapping(mapping.id);
                    }}
                  >
                    <DeleteOutlined className="text-xs" />
                  </button>
                </Tooltip>
              }
            >
              <span>{mapping.mediaLibrary?.name || t("Unknown")}</span>
            </Chip>
          ))}
        </div>
      )}
    </div>
  );
};

MediaLibraryMappings.displayName = "MediaLibraryMappings";

export default MediaLibraryMappings;
