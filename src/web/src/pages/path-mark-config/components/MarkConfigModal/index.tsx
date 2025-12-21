"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { MarkConfigModalProps } from "./types";

import { useState, useCallback, useRef, useEffect } from "react";
import { Trans, useTranslation } from "react-i18next";
import { Modal, Switch, DurationInput, Button, toast } from "@/components/bakaui";
import { PathMarkType } from "@/sdk/constants";
import { SaveOutlined, SyncOutlined, DownOutlined } from "@ant-design/icons";

import { parseMarkConfig, buildConfigJson } from "./utils";
import ResourceMarkConfig from "./components/ResourceMarkConfig";
import PropertyMarkConfig from "./components/PropertyMarkConfig";
import MediaLibraryMarkConfig from "./components/MediaLibraryMarkConfig";
import { usePreview } from "./hooks/usePreview";
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
  const [showScrollIndicator, setShowScrollIndicator] = useState(false);
  const contentRef = useRef<HTMLDivElement>(null);

  const preview = usePreview(rootPath, markType, config, 500, rootPaths);

  // Check if content is scrollable and update scroll indicator
  useEffect(() => {
    const checkScrollable = () => {
      const el = contentRef.current;
      if (el) {
        const isScrollable = el.scrollHeight > el.clientHeight;
        const isAtBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 10;
        setShowScrollIndicator(isScrollable && !isAtBottom);
      }
    };

    checkScrollable();

    const el = contentRef.current;
    if (el) {
      el.addEventListener("scroll", checkScrollable);
      // Also check on resize
      const resizeObserver = new ResizeObserver(checkScrollable);
      resizeObserver.observe(el);

      return () => {
        el.removeEventListener("scroll", checkScrollable);
        resizeObserver.disconnect();
      };
    }
  }, [config, enableExpiration]);

  const handleSave = useCallback(async () => {
    const newMark: Partial<BakabaseAbstractionsModelsDomainPathMark> = {
      type: markType,
      priority,
      configJson: buildConfigJson(config, markType),
      expiresInSeconds: enableExpiration ? expiresInSeconds : undefined,
    };
    await onSave?.(newMark as BakabaseAbstractionsModelsDomainPathMark);
    return true;
  }, [markType, priority, config, enableExpiration, expiresInSeconds, onSave]);

  const handleSyncNow = useCallback(async () => {
    if (!mark?.id) return;
    setSyncing(true);
    try {
      await BApi.pathMark.startPathMarkSync([mark.id]);
      toast.success(t("Sync started"));
    } catch (e) {
      toast.danger(t("Failed to start sync"));
    } finally {
      setSyncing(false);
    }
  }, [mark?.id, t]);

  const updateConfig = useCallback((updates: Partial<typeof config>) => {
    setConfig(prev => ({ ...prev, ...updates }));
  }, []);

  const scrollToBottom = () => {
    contentRef.current?.scrollTo({
      top: contentRef.current.scrollHeight,
      behavior: "smooth",
    });
  };

  // Determine paths count for title
  const pathCount = rootPaths?.length ?? (rootPath ? 1 : 0);

  // Prepare preview data for child components
  const previewData = {
    loading: preview.loading,
    results: preview.results,
    resultsByPath: preview.resultsByPath,
    isMultiplePaths: preview.isMultiplePaths,
    error: preview.error,
    applyScope: preview.applyScope,
  };

  return (
    <Modal
      defaultVisible
      size="3xl"
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
      <div className="relative overflow-hidden">
        <div
          ref={contentRef}
          className="flex flex-col gap-2 max-h-[60vh] overflow-y-auto overflow-x-hidden pr-1"
        >
          {/* Type-specific configuration */}
          {markType === PathMarkType.Resource ? (
            <ResourceMarkConfig
              config={config}
              updateConfig={updateConfig}
              t={t}
              priority={priority}
              onPriorityChange={setPriority}
              preview={previewData}
            />
          ) : markType === PathMarkType.Property ? (
            <PropertyMarkConfig
              config={config}
              updateConfig={updateConfig}
              t={t}
              priority={priority}
              onPriorityChange={setPriority}
              preview={previewData}
            />
          ) : markType === PathMarkType.MediaLibrary ? (
            <MediaLibraryMarkConfig
              config={config}
              updateConfig={updateConfig}
              t={t}
              priority={priority}
              onPriorityChange={setPriority}
              preview={previewData}
            />
          ) : null}

          {/* Expiration Configuration */}
          <div className="border-t border-default-200 pt-3 mt-2">
            <div className="flex items-center gap-2 mb-1">
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
            </div>
            <div className="text-xs text-default-400 ml-10 mb-2">
              {t("After sync completes, the mark will be re-synced after the specified time. If set too short, it may cause frequent re-syncing and affect performance.")}
            </div>
            {enableExpiration && (
              <div className="ml-10">
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

        {/* Scroll indicator */}
        {showScrollIndicator && (
          <div
            className="absolute bottom-0 left-0 right-0 flex justify-center items-center py-1 bg-gradient-to-t from-background to-transparent cursor-pointer hover:from-primary-50 transition-colors"
            onClick={scrollToBottom}
          >
            <div className="flex items-center gap-1 text-xs text-primary-500 animate-bounce">
              <DownOutlined />
              <span>{t("Scroll for more")}</span>
              <DownOutlined />
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
};

MarkConfigModal.displayName = "MarkConfigModal";

export default MarkConfigModal;
