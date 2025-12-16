"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { MarkConfigModalProps } from "./types";

import React, { useState, useCallback } from "react";
import { Trans, useTranslation } from "react-i18next";
import { Modal } from "@/components/bakaui";
import { PathMarkType } from "@/sdk/constants";
import { SaveOutlined } from "@ant-design/icons";

import { parseMarkConfig, buildConfigJson } from "./utils";
import ResourceMarkConfig from "./components/ResourceMarkConfig";
import PropertyMarkConfig from "./components/PropertyMarkConfig";
import MediaLibraryMarkConfig from "./components/MediaLibraryMarkConfig";
import { ResourceTerm } from "@/components/Chips/Terms";

const getMarkTypeLabel = (markType: PathMarkType, t: (key: string) => string) => {
  switch (markType) {
    case PathMarkType.Resource:
      return null; // Will use ResourceTerm component
    case PathMarkType.Property:
      return t("Property");
    case PathMarkType.MediaLibrary:
      return t("Media Library");
    default:
      return t("Unknown");
  }
};

const MarkConfigModal = ({ mark, markType, rootPath, rootPaths, onSave, onDestroyed }: MarkConfigModalProps) => {
  const { t } = useTranslation();

  const initialConfig = parseMarkConfig(mark?.configJson);
  const [priority, setPriority] = useState(mark?.priority ?? 10);
  const [config, setConfig] = useState(initialConfig);

  const handleSave = useCallback(async () => {
    const newMark: BakabaseAbstractionsModelsDomainPathMark = {
      type: markType,
      priority,
      configJson: buildConfigJson(config, markType),
    };
    await onSave?.(newMark);
    return true;
  }, [markType, priority, config, onSave]);

  const updateConfig = useCallback((updates: Partial<typeof config>) => {
    setConfig(prev => ({ ...prev, ...updates }));
  }, []);

  // Determine paths count for title
  const pathCount = rootPaths?.length ?? (rootPath ? 1 : 0);
  const typeLabel = getMarkTypeLabel(markType, t);

  return (
    <Modal
      defaultVisible
      size="lg"
      title={
        <span className="flex items-center gap-1 whitespace-nowrap">
          <Trans
            i18nKey={mark ? "Edit <type></type> Mark" : "Add <type></type> Mark"}
            components={{
              type: markType === PathMarkType.Resource ? <ResourceTerm size="lg" /> : <span>{typeLabel}</span>,
            }}
          />
          {pathCount > 1 && (
            <span className="text-default-400 text-sm ml-2">
              ({t("{{count}} paths", { count: pathCount })})
            </span>
          )}
        </span>
      }
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: t("Save"),
          startContent: <SaveOutlined />,
        },
      }}
      onOk={handleSave}
      onDestroyed={onDestroyed}
    >
      <div className="flex flex-col gap-2">
        {/* Type-specific configuration */}
        {markType === PathMarkType.Resource ? (
          <ResourceMarkConfig
            config={config}
            updateConfig={updateConfig}
            rootPath={rootPath}
            rootPaths={rootPaths}
            t={t}
            priority={priority}
            onPriorityChange={setPriority}
          />
        ) : markType === PathMarkType.Property ? (
          <PropertyMarkConfig
            config={config}
            updateConfig={updateConfig}
            rootPath={rootPath}
            rootPaths={rootPaths}
            t={t}
            priority={priority}
            onPriorityChange={setPriority}
          />
        ) : markType === PathMarkType.MediaLibrary ? (
          <MediaLibraryMarkConfig
            config={config}
            updateConfig={updateConfig}
            rootPath={rootPath}
            rootPaths={rootPaths}
            t={t}
            priority={priority}
            onPriorityChange={setPriority}
          />
        ) : null}
      </div>
    </Modal>
  );
};

MarkConfigModal.displayName = "MarkConfigModal";

export default MarkConfigModal;
