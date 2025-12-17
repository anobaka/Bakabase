"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { MarkConfigModalProps } from "./types";

import React, { useState, useCallback } from "react";
import { Trans, useTranslation } from "react-i18next";
import { Modal, Switch, DurationInput, Tooltip, Button, toast } from "@/components/bakaui";
import { PathMarkType } from "@/sdk/constants";
import { SaveOutlined, SyncOutlined, InfoCircleOutlined } from "@ant-design/icons";

import { parseMarkConfig, buildConfigJson } from "./utils";
import ResourceMarkConfig from "./components/ResourceMarkConfig";
import PropertyMarkConfig from "./components/PropertyMarkConfig";
import MediaLibraryMarkConfig from "./components/MediaLibraryMarkConfig";
import { ResourceTerm, PropertyTerm, MediaLibraryTerm } from "@/components/Chips/Terms";
import BApi from "@/sdk/BApi";

const DEFAULT_EXPIRES_IN_SECONDS = 3600; // 1 hour

const MarkConfigModal = ({ mark, markType, rootPath, rootPaths, onSave, onDestroyed }: MarkConfigModalProps) => {
  const { t } = useTranslation();

  const initialConfig = parseMarkConfig(mark?.configJson);
  const [priority, setPriority] = useState(mark?.priority ?? 10);
  const [config, setConfig] = useState(initialConfig);
  const [enableExpiration, setEnableExpiration] = useState(mark?.expiresInSeconds != null && mark.expiresInSeconds > 0);
  const [expiresInSeconds, setExpiresInSeconds] = useState(mark?.expiresInSeconds ?? DEFAULT_EXPIRES_IN_SECONDS);
  const [syncing, setSyncing] = useState(false);

  const handleSave = useCallback(async () => {
    const newMark: BakabaseAbstractionsModelsDomainPathMark = {
      type: markType,
      priority,
      configJson: buildConfigJson(config, markType),
      expiresInSeconds: enableExpiration ? expiresInSeconds : undefined,
    };
    await onSave?.(newMark);
    return true;
  }, [markType, priority, config, enableExpiration, expiresInSeconds, onSave]);

  const handleSyncNow = useCallback(async () => {
    if (!mark?.id) return;
    setSyncing(true);
    try {
      await BApi.pathMark.startPathMarkSync([mark.id]);
      toast.success(t("Sync started"));
    } catch (e) {
      toast.error(t("Failed to start sync"));
    } finally {
      setSyncing(false);
    }
  }, [mark?.id, t]);

  const updateConfig = useCallback((updates: Partial<typeof config>) => {
    setConfig(prev => ({ ...prev, ...updates }));
  }, []);

  // Determine paths count for title
  const pathCount = rootPaths?.length ?? (rootPath ? 1 : 0);

  return (
    <Modal
      defaultVisible
      size="lg"
      title={
        <span className="flex items-center gap-1 whitespace-nowrap">
          <Trans
            i18nKey={mark ? "Edit <type></type> Mark" : "Add <type></type> Mark"}
            components={{
              type: markType === PathMarkType.Resource ? <ResourceTerm size="lg" /> :
                    markType === PathMarkType.Property ? <PropertyTerm size="lg" /> :
                    markType === PathMarkType.MediaLibrary ? <MediaLibraryTerm size="lg" /> :
                    <span>{t("Unknown")}</span>,
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
        startContent: mark?.id ? (
          <Button
            color="primary"
            variant="flat"
            size="sm"
            isLoading={syncing}
            startContent={<SyncOutlined />}
            onClick={handleSyncNow}
          >
            {t("Sync Now")}
          </Button>
        ) : undefined,
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

        {/* Expiration Configuration */}
        <div className="border-t border-default-200 pt-3 mt-2">
          <div className="flex items-center gap-2 mb-2">
            <Switch
              size="sm"
              isSelected={enableExpiration}
              onValueChange={(checked) => {
                setEnableExpiration(checked);
                if (checked && expiresInSeconds <= 0) {
                  setExpiresInSeconds(DEFAULT_EXPIRES_IN_SECONDS);
                }
              }}
            />
            <span className="text-sm font-medium">{t("Enable expiration")}</span>
            <Tooltip content={t("After sync completes, the mark will be re-synced after the specified time. If set too short, it may cause frequent re-syncing and affect performance.")}>
              <InfoCircleOutlined className="text-default-400 cursor-help" />
            </Tooltip>
          </div>
          {enableExpiration && (
            <div className="ml-6">
              <DurationInput
                value={expiresInSeconds}
                onChange={setExpiresInSeconds}
                minValue={60}
                size="sm"
              />
            </div>
          )}
        </div>
      </div>
    </Modal>
  );
};

MarkConfigModal.displayName = "MarkConfigModal";

export default MarkConfigModal;
