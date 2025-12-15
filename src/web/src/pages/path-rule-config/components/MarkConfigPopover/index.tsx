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
import { ResourceTerm } from "@/components/Chips/Terms";

const MarkConfigModal = ({ mark, markType, rootPath, onSave, onDestroyed }: MarkConfigModalProps) => {
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

  return (
    <Modal
      defaultVisible
      size="lg"
      title={
        <span className="flex items-center gap-1 whitespace-nowrap">
          <Trans
            i18nKey={mark ? "Edit <type></type> Mark" : "Add <type></type> Mark"}
            components={{
              type: markType === PathMarkType.Resource ? <ResourceTerm size="xl" /> : <span>{t("Property")}</span>,
            }}
          />
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
            t={t}
            priority={priority}
            onPriorityChange={setPriority}
          />
        ) : (
          <PropertyMarkConfig
            config={config}
            updateConfig={updateConfig}
            rootPath={rootPath}
            t={t}
            priority={priority}
            onPriorityChange={setPriority}
          />
        )}
      </div>
    </Modal>
  );
};

MarkConfigModal.displayName = "MarkConfigModal";

export default MarkConfigModal;
