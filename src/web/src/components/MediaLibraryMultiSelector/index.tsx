"use client";

import type { DestroyableProps } from "@/components/bakaui/types";

import React, { useEffect, useState, useMemo } from "react";
import { useTranslation } from "react-i18next";
import {
  CheckCircleFilled,
  MinusCircleFilled,
  CloseCircleOutlined,
  UndoOutlined,
} from "@ant-design/icons";

import { buildLogger } from "@/components/utils";
import BApi from "@/sdk/BApi";
import { Button, Modal, Spinner, Tooltip } from "@/components/bakaui";

const log = buildLogger("MediaLibraryMultiSelector");

type Props = {
  resourceIds: number[];
  onSubmit?: () => void;
} & DestroyableProps;

type Library = {
  id: number;
  name: string;
};

// Association state for each media library
enum AssociationState {
  None = "none", // Not associated with any selected resources
  Partial = "partial", // Associated with some selected resources
  Full = "full", // Associated with all selected resources
}

const MediaLibraryMultiSelector = (props: Props) => {
  const { t } = useTranslation();
  const { resourceIds, onSubmit } = props;

  const [visible, setVisible] = useState(true);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [libraries, setLibraries] = useState<Library[]>([]);

  // Original state from server
  const [originalMappings, setOriginalMappings] = useState<Record<number, number[]>>({});

  // Current edited state: mediaLibraryId -> resourceIds that should be associated
  const [currentAssociations, setCurrentAssociations] = useState<Record<number, Set<number>>>({});

  const init = async () => {
    setLoading(true);
    try {
      // Load all media libraries
      const mlResponse = await BApi.mediaLibraryV2.getAllMediaLibraryV2();
      const mls = mlResponse.data ?? [];

      setLibraries(
        mls.map((ml) => ({
          id: ml.id!,
          name: ml.name || `Library ${ml.id}`,
        })),
      );

      // Load current mappings for selected resources
      const mappingsResponse = await BApi.resource.getResourceMediaLibraryMappings(resourceIds);
      const rawMappings = mappingsResponse.data ?? {};

      // Convert string keys to number keys and filter out null values
      const mappings: Record<number, number[]> = {};

      for (const [resourceIdStr, mlIds] of Object.entries(rawMappings)) {
        const resourceId = parseInt(resourceIdStr, 10);

        mappings[resourceId] = mlIds ?? [];
      }
      setOriginalMappings(mappings);

      // Initialize current associations from mappings
      const associations: Record<number, Set<number>> = {};

      for (const ml of mls) {
        associations[ml.id!] = new Set();
      }
      for (const [resourceId, mlIds] of Object.entries(mappings)) {
        const resId = parseInt(resourceId.toString(), 10);

        for (const mlId of mlIds) {
          if (associations[mlId]) {
            associations[mlId].add(resId);
          }
        }
      }
      setCurrentAssociations(associations);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    init();
  }, []);

  // Calculate association state for each library
  const getAssociationState = (libraryId: number): AssociationState => {
    const associatedResources = currentAssociations[libraryId];

    if (!associatedResources || associatedResources.size === 0) {
      return AssociationState.None;
    }
    if (associatedResources.size === resourceIds.length) {
      return AssociationState.Full;
    }

    return AssociationState.Partial;
  };

  // Handle library click - cycle through states
  const handleLibraryClick = (libraryId: number) => {
    const state = getAssociationState(libraryId);

    setCurrentAssociations((prev) => {
      const newAssociations = { ...prev };
      const newSet = new Set(prev[libraryId]);

      switch (state) {
        case AssociationState.None:
          // None -> Full: add all resources
          for (const resourceId of resourceIds) {
            newSet.add(resourceId);
          }
          break;
        case AssociationState.Partial:
          // Partial -> Full: add all resources
          for (const resourceId of resourceIds) {
            newSet.add(resourceId);
          }
          break;
        case AssociationState.Full:
          // Full -> None: remove all resources
          newSet.clear();
          break;
      }

      newAssociations[libraryId] = newSet;

      return newAssociations;
    });
  };

  // Reset to original state
  const handleReset = () => {
    const associations: Record<number, Set<number>> = {};

    for (const lib of libraries) {
      associations[lib.id] = new Set();
    }
    for (const [resourceIdStr, mlIds] of Object.entries(originalMappings)) {
      const resourceId = parseInt(resourceIdStr, 10);

      for (const mlId of mlIds) {
        if (associations[mlId]) {
          associations[mlId].add(resourceId);
        }
      }
    }
    setCurrentAssociations(associations);
  };

  // Check if there are changes
  const hasChanges = useMemo(() => {
    for (const lib of libraries) {
      const original = new Set<number>();

      for (const [resourceIdStr, mlIds] of Object.entries(originalMappings)) {
        const resourceId = parseInt(resourceIdStr, 10);

        if (mlIds.includes(lib.id)) {
          original.add(resourceId);
        }
      }
      const current = currentAssociations[lib.id] || new Set();

      if (original.size !== current.size) return true;
      for (const id of original) {
        if (!current.has(id)) return true;
      }
    }

    return false;
  }, [libraries, originalMappings, currentAssociations]);

  // Submit changes
  const handleSubmit = async () => {
    setSubmitting(true);
    try {
      // Convert current associations to the format needed by the API
      // We need to call setResourceMediaLibraries for all selected resources
      const mediaLibraryIds = libraries
        .filter((lib) => {
          const state = getAssociationState(lib.id);

          return state === AssociationState.Full;
        })
        .map((lib) => lib.id);

      await BApi.resource.setResourceMediaLibraries({
        ids: resourceIds,
        mediaLibraryIds,
      });

      onSubmit?.();
      setVisible(false);
    } finally {
      setSubmitting(false);
    }
  };

  const renderLibraryButton = (lib: Library) => {
    const state = getAssociationState(lib.id);

    let icon: React.ReactNode;
    let color: "default" | "primary" | "success" | "warning";
    let tooltipText: string;

    switch (state) {
      case AssociationState.Full:
        icon = <CheckCircleFilled className="text-success" />;
        color = "success";
        tooltipText = t<string>("All selected resources are in this library");
        break;
      case AssociationState.Partial:
        icon = <MinusCircleFilled className="text-warning" />;
        color = "warning";
        tooltipText = t<string>("Some selected resources are in this library");
        break;
      case AssociationState.None:
      default:
        icon = <CloseCircleOutlined className="text-default-400" />;
        color = "default";
        tooltipText = t<string>("No selected resources are in this library");
        break;
    }

    return (
      <Tooltip key={lib.id} content={tooltipText}>
        <Button
          className="m-1"
          color={color}
          size="sm"
          startContent={icon}
          variant={state === AssociationState.None ? "flat" : "solid"}
          onClick={() => handleLibraryClick(lib.id)}
        >
          {lib.name}
        </Button>
      </Tooltip>
    );
  };

  return (
    <Modal
      footer={false}
      size="lg"
      title={t<string>("Set media libraries for {{count}} resources", {
        count: resourceIds.length,
      })}
      visible={visible}
      onClose={() => setVisible(false)}
      onDestroyed={props.onDestroyed}
    >
      {loading ? (
        <div className="flex justify-center py-8">
          <Spinner />
        </div>
      ) : (
        <div>
          <div className="mb-4 text-sm text-default-500">
            {t<string>(
              "Click to toggle. Green = all associated, Yellow = partially associated, Gray = not associated",
            )}
          </div>

          <div className="flex flex-wrap">{libraries.map((lib) => renderLibraryButton(lib))}</div>

          {libraries.length === 0 && (
            <div className="py-4 text-center text-default-400">
              {t<string>("No media libraries found")}
            </div>
          )}

          <div className="mt-6 flex justify-end gap-2">
            <Button
              color="default"
              disabled={!hasChanges}
              startContent={<UndoOutlined />}
              variant="flat"
              onClick={handleReset}
            >
              {t<string>("Reset")}
            </Button>
            <Button
              color="primary"
              disabled={!hasChanges}
              isLoading={submitting}
              onClick={handleSubmit}
            >
              {t<string>("Submit")}
            </Button>
          </div>
        </div>
      )}
    </Modal>
  );
};

MediaLibraryMultiSelector.displayName = "MediaLibraryMultiSelector";

export default MediaLibraryMultiSelector;
