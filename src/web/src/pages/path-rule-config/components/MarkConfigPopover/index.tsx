"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { MarkConfigModalProps } from "./types";

import React, { useState, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { Input, Modal } from "@/components/bakaui";
import { PathMarkType } from "@/sdk/constants";
import { SaveOutlined } from "@ant-design/icons";

import { parseMarkConfig, buildConfigJson } from "./utils";
import ResourceMarkConfig from "./components/ResourceMarkConfig";
import PropertyMarkConfig from "./components/PropertyMarkConfig";

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
      title={`${mark ? t("Edit Mark") : t("Add Mark")} - ${markType === PathMarkType.Resource ? t("Resource") : t("Property")}`}
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
      <div className="flex flex-col gap-4">
        {/* Type-specific configuration */}
        {markType === PathMarkType.Resource ? (
          <ResourceMarkConfig
            config={config}
            updateConfig={updateConfig}
            rootPath={rootPath}
            t={t}
          />
        ) : (
          <PropertyMarkConfig
            config={config}
            updateConfig={updateConfig}
            rootPath={rootPath}
            t={t}
          />
        )}

        {/* Priority - Shared */}
        <div className="border-t border-default-200 pt-3">
          <Input
            label={t("Priority")}
            description={t("Higher priority marks are applied first when multiple marks match")}
            type="number"
            size="sm"
            value={String(priority)}
            onValueChange={(v) => setPriority(parseInt(v) || 10)}
          />
        </div>
      </div>
    </Modal>
  );
};

MarkConfigModal.displayName = "MarkConfigModal";

export default MarkConfigModal;
