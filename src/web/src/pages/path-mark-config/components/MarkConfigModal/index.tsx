"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { MarkConfigModalProps } from "./types";

import { useState, useCallback, useRef, useEffect } from "react";
import { Trans, useTranslation } from "react-i18next";
import { Modal, Switch, DurationInput, Button, toast } from "@/components/bakaui";
import { PathMarkType, PathMarkApplyScope, PropertyValueType } from "@/sdk/constants";
import { SaveOutlined, SyncOutlined, DownOutlined } from "@ant-design/icons";

import { parseMarkConfig, buildConfigJson } from "./utils";
import ResourceMarkConfig from "./components/ResourceMarkConfig";
import PropertyMarkConfig from "./components/PropertyMarkConfig";
import MediaLibraryMarkConfig from "./components/MediaLibraryMarkConfig";
import PreviewResults from "./components/PreviewResults";
import { usePreview } from "./hooks/usePreview";
import { ResourceTerm, PropertyTerm, MediaLibraryTerm } from "@/components/Chips/Terms";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";

const DEFAULT_EXPIRES_IN_SECONDS = 3600; // 1 hour

const MarkConfigModal = ({ mark, markType, rootPath, rootPaths, onSave, onDestroyed }: MarkConfigModalProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const initialConfig = parseMarkConfig(mark?.configJson);
  // For new MediaLibrary marks, default applyScope to MatchedAndSubdirectories
  if (!mark && markType === PathMarkType.MediaLibrary) {
    initialConfig.applyScope = PathMarkApplyScope.MatchedAndSubdirectories;
  }
  const [priority, setPriority] = useState(mark?.priority ?? 10);
  const [config, setConfig] = useState(initialConfig);
  const [enableExpiration, setEnableExpiration] = useState(mark?.expiresInSeconds != null && mark.expiresInSeconds > 0);
  const [expiresInSeconds, setExpiresInSeconds] = useState(mark?.expiresInSeconds ?? DEFAULT_EXPIRES_IN_SECONDS);
  const [syncing, setSyncing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [visible, setVisible] = useState(true);
  const confirmingRef = useRef(false);
  const [showScrollIndicator, setShowScrollIndicator] = useState(false);
  const contentRef = useRef<HTMLDivElement>(null);

  const preview = usePreview(rootPath, markType, config, 500, rootPaths);

  // Resolve property name for Property marks
  const [propertyName, setPropertyName] = useState<string | undefined>();
  useEffect(() => {
    if (markType !== PathMarkType.Property || !config.propertyId || !config.propertyPool) {
      setPropertyName(undefined);
      return;
    }
    BApi.property.getPropertiesByPool(config.propertyPool).then(rsp => {
      const prop = rsp.data?.find(p => p.id === config.propertyId);
      setPropertyName(prop?.name ?? undefined);
    });
  }, [markType, config.propertyPool, config.propertyId]);

  // Resolve media library name for MediaLibrary marks (Fixed mode)
  const [mediaLibraryName, setMediaLibraryName] = useState<string | undefined>();
  useEffect(() => {
    if (markType !== PathMarkType.MediaLibrary || config.mediaLibraryValueType !== PropertyValueType.Fixed || !config.mediaLibraryId) {
      setMediaLibraryName(undefined);
      return;
    }
    BApi.mediaLibraryV2.getAllMediaLibraryV2().then(rsp => {
      const lib = rsp.data?.find(l => l.id === config.mediaLibraryId);
      setMediaLibraryName(lib?.name ?? undefined);
    });
  }, [markType, config.mediaLibraryValueType, config.mediaLibraryId]);

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

  // Check if the config has validation issues (unset property/value for Property or MediaLibrary marks)
  const getConfigValidationIssues = useCallback((): { hasPropertyIssue: boolean; hasValueIssue: boolean } => {
    if (markType === PathMarkType.Property) {
      const hasPropertyIssue = !config.propertyId;
      const hasValueIssue = config.valueType === PropertyValueType.Fixed
        ? !config.fixedValue
        : false; // Dynamic values are computed from paths, no validation needed
      return { hasPropertyIssue, hasValueIssue };
    }
    if (markType === PathMarkType.MediaLibrary) {
      const hasPropertyIssue = false; // MediaLibrary marks don't have a "property" to select
      const hasValueIssue = config.mediaLibraryValueType === PropertyValueType.Fixed
        ? !config.mediaLibraryId
        : false;
      return { hasPropertyIssue, hasValueIssue };
    }
    return { hasPropertyIssue: false, hasValueIssue: false };
  }, [markType, config]);

  const closeOuterModal = useCallback(() => {
    setVisible(false);
  }, []);

  const handleSave = useCallback(() => {
    const issues = getConfigValidationIssues();
    confirmingRef.current = true;

    // Show confirmation modal with preview
    createPortal(Modal, {
      defaultVisible: true,
      size: "3xl",
      title: t("pathMarkConfig.confirm.previewTitle"),
      children: (
        <div className="flex flex-col gap-3">
          {/* Validation warnings */}
          {(issues.hasPropertyIssue || issues.hasValueIssue) && (
            <div className="bg-danger-50 border border-danger-200 rounded-lg p-3">
              <p className="text-danger-700 font-medium text-sm">
                {t("pathMarkConfig.confirm.validationError")}
              </p>
              {issues.hasPropertyIssue && (
                <p className="text-danger-600 text-xs mt-1">
                  {t("pathMarkConfig.confirm.propertyNotSet")}
                </p>
              )}
              {issues.hasValueIssue && (
                <p className="text-danger-600 text-xs mt-1">
                  {markType === PathMarkType.MediaLibrary
                    ? t("pathMarkConfig.confirm.mediaLibraryNotSet")
                    : t("pathMarkConfig.confirm.propertyValueNotSet")}
                </p>
              )}
            </div>
          )}

          {/* Preview with danger highlighting for missing values */}
          {preview.results.length > 0 ? (
            <div className="max-h-[40vh] overflow-y-auto">
              <PreviewResults
                loading={preview.loading}
                results={preview.results}
                resultsByPath={preview.resultsByPath}
                isMultiplePaths={preview.isMultiplePaths}
                error={preview.error}
                markType={markType}
                t={t}
                applyScope={preview.applyScope}
                highlightMissingValues={issues.hasPropertyIssue || issues.hasValueIssue}
                propertyName={propertyName}
                mediaLibraryName={mediaLibraryName}
              />
            </div>
          ) : (
            <div className="bg-warning-50 text-warning-600 rounded p-2 text-xs">
              {t("pathMarkConfig.warning.noMatches")}
            </div>
          )}

          <p className="text-xs text-default-500">
            {t("pathMarkConfig.confirm.previewDescription")}
          </p>
        </div>
      ),
      footer: {
        actions: ["cancel", "ok"],
        okProps: {
          children: t("common.action.confirm"),
          color: "primary",
          isDisabled: issues.hasPropertyIssue || issues.hasValueIssue,
        },
      },
      onOk: async () => {
        setSaving(true);
        try {
          const newMark: Partial<BakabaseAbstractionsModelsDomainPathMark> = {
            type: markType,
            priority,
            configJson: buildConfigJson(config, markType),
            expiresInSeconds: enableExpiration ? expiresInSeconds : undefined,
          };
          await onSave?.(newMark as BakabaseAbstractionsModelsDomainPathMark);
          // Close the outer MarkConfigModal after successful save
          closeOuterModal();
        } finally {
          setSaving(false);
        }
      },
      onDestroyed: () => {
        confirmingRef.current = false;
      },
    });
  }, [markType, priority, config, enableExpiration, expiresInSeconds, onSave, closeOuterModal, preview, createPortal, t, getConfigValidationIssues]);

  const handleSyncNow = useCallback(async () => {
    if (!mark?.id) return;
    setSyncing(true);
    try {
      await BApi.pathMark.startPathMarkSync([mark.id]);
      toast.success(t("pathMarkConfig.success.syncStarted"));
    } catch (e) {
      toast.danger(t("pathMarkConfig.error.syncFailed"));
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
      visible={visible}
      size="3xl"
      onClose={() => {
        // Block close while confirmation modal is open (HeroUI propagates
        // backdrop/ESC close events to all stacked modals)
        if (confirmingRef.current) return;
        closeOuterModal();
      }}
      title={
        <span className="flex items-center gap-1 whitespace-nowrap">
          <Trans
            i18nKey={mark ? "Edit <type></type> Mark" : "Add <type></type> Mark"}
            components={{
              type: markType === PathMarkType.Resource ? <ResourceTerm size="lg" /> :
                    markType === PathMarkType.Property ? <PropertyTerm size="lg" /> :
                    markType === PathMarkType.MediaLibrary ? <MediaLibraryTerm size="lg" /> :
                    <span>{t("pathMarkConfig.status.unknown")}</span>,
            }}
          />
          {pathCount > 1 && (
            <span className="text-default-400 text-sm ml-2">
              ({t("pathMarkConfig.label.pathCount", { count: pathCount })})
            </span>
          )}
        </span>
      }
      footer={
        <div className="flex items-center justify-between w-full">
          <div>
            {mark?.id && (
              <Button
                color="primary"
                variant="flat"
                size="sm"
                isLoading={syncing}
                startContent={<SyncOutlined />}
                onPress={handleSyncNow}
              >
                {t("pathMarkConfig.action.syncNow")}
              </Button>
            )}
          </div>
          <div className="flex items-center gap-2">
            <Button color="danger" variant="light" onPress={closeOuterModal}>
              {t("Close")}
            </Button>
            <Button
              color="primary"
              isLoading={saving}
              startContent={<SaveOutlined />}
              onPress={handleSave}
            >
              {t("common.action.save")}
            </Button>
          </div>
        </div>
      }
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
              <span className="text-sm font-medium">{t("pathMarkConfig.label.scheduledSync")}</span>
            </div>
            <div className="text-xs text-default-400 ml-10 mb-2">
              {t("pathMarkConfig.tip.expirationDescription")}
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
              <span>{t("pathMarkConfig.label.scrollForMore")}</span>
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
