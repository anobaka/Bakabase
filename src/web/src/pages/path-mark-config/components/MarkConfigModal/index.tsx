"use client";

import type { ReactNode } from "react";
import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";
import type { MarkConfigModalProps } from "./types";

import { useState, useCallback } from "react";
import { Trans, useTranslation } from "react-i18next";
import { SaveOutlined, SyncOutlined } from "@ant-design/icons";

import {
  parseMarkConfig,
  buildConfigJson,
  hasValidValueExtractor as hasValidValueExtractorConfig,
} from "./utils";
import ResourceMarkConfig from "./components/ResourceMarkConfig";
import PropertyMarkConfig from "./components/PropertyMarkConfig";
import MediaLibraryMarkConfig from "./components/MediaLibraryMarkConfig";
import MatchModeSelector from "./components/MatchModeSelector";
import PreviewResults from "./components/PreviewResults";
import { usePreview } from "./hooks/usePreview";

import { PathMarkType, PathMarkApplyScope } from "@/sdk/constants";
import { Modal, Switch, DurationInput, NumberInput, Button, toast } from "@/components/bakaui";
import { ResourceTerm, PropertyTerm, MediaLibraryTerm } from "@/components/Chips/Terms";
import { HelpCenterButton } from "@/components/HelpCenter";
import BApi from "@/sdk/BApi";

const DEFAULT_EXPIRES_IN_SECONDS = 3600;

const ConfigSection = ({
  number,
  title,
  description,
  children,
}: {
  number: string;
  title: string;
  description?: string;
  children: ReactNode;
}) => (
  <section className="rounded-2xl border border-default-200 bg-content1 p-4 sm:p-5">
    <div className="mb-4 flex items-start gap-3">
      <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-lg bg-primary-50 text-xs font-semibold text-primary-600">
        {number}
      </span>
      <div>
        <h3 className="text-sm font-semibold text-default-800">{title}</h3>
        {description && (
          <p className="mt-1 text-xs leading-relaxed text-default-500">{description}</p>
        )}
      </div>
    </div>
    {children}
  </section>
);

const MarkConfigModal = ({
  mark,
  markType,
  rootPath,
  rootPaths,
  onSave,
  onDestroyed,
}: MarkConfigModalProps) => {
  const { t } = useTranslation();
  const [config, setConfig] = useState(() => {
    const initial = parseMarkConfig(mark?.configJson, markType);

    if (!mark && markType === PathMarkType.MediaLibrary)
      initial.applyScope = PathMarkApplyScope.MatchedAndSubdirectories;

    return initial;
  });
  const [priority, setPriority] = useState(mark?.priority ?? 10);
  const [enableExpiration, setEnableExpiration] = useState((mark?.expiresInSeconds ?? 0) > 0);
  const [expiresInSeconds, setExpiresInSeconds] = useState(
    mark?.expiresInSeconds ?? DEFAULT_EXPIRES_IN_SECONDS,
  );
  const [syncing, setSyncing] = useState(false);
  const paths = rootPaths?.length
    ? rootPaths
    : [rootPath ?? mark?.path].filter((path): path is string => Boolean(path));
  const pathCount = paths.length;
  const preview = usePreview(rootPath ?? mark?.path, markType, config, 500, rootPaths);
  const hasValidValueExtractor = hasValidValueExtractorConfig(config, markType);
  const canSave = hasValidValueExtractor && !preview.configurationHint;
  const isResource = markType === PathMarkType.Resource;

  const handleSave = useCallback(async () => {
    if (!canSave) return false;

    const newMark: Partial<BakabaseAbstractionsModelsDomainPathMark> = {
      type: markType,
      priority,
      configJson: buildConfigJson(config, markType),
      expiresInSeconds: enableExpiration ? expiresInSeconds : undefined,
    };

    await onSave?.(newMark as BakabaseAbstractionsModelsDomainPathMark);

    return true;
  }, [markType, priority, config, enableExpiration, expiresInSeconds, onSave, canSave]);

  const handleSyncNow = useCallback(async () => {
    if (!mark?.id) return;
    setSyncing(true);
    try {
      await BApi.pathMark.startPathMarkSync([mark.id]);
      toast.success(t("pathMarkConfig.success.syncStarted"));
    } catch {
      toast.danger(t("pathMarkConfig.error.syncFailed"));
    } finally {
      setSyncing(false);
    }
  }, [mark?.id, t]);

  const updateConfig = useCallback((updates: Partial<typeof config>) => {
    setConfig((prev) => ({ ...prev, ...updates }));
  }, []);

  return (
    <Modal
      defaultVisible
      classNames={{
        header: "border-b border-default-200",
        footer: "border-t border-default-200",
        body: "bg-default-50/60",
      }}
      footer={{
        actions: ["cancel", "ok"],
        okProps: {
          children: t("common.action.save"),
          isDisabled: !canSave,
          startContent: <SaveOutlined />,
        },
        startContent: mark?.id ? (
          <Button
            color="primary"
            isLoading={syncing}
            size="sm"
            startContent={<SyncOutlined />}
            variant="flat"
            onPress={handleSyncNow}
          >
            {t("pathMarkConfig.action.syncSaved")}
          </Button>
        ) : undefined,
      }}
      size="6xl"
      title={
        <span className="flex items-center gap-1 whitespace-nowrap">
          <Trans
            components={{
              type:
                markType === PathMarkType.Resource ? (
                  <ResourceTerm size="lg" />
                ) : markType === PathMarkType.Property ? (
                  <PropertyTerm size="lg" />
                ) : markType === PathMarkType.MediaLibrary ? (
                  <MediaLibraryTerm size="lg" />
                ) : (
                  <span>{t("pathMarkConfig.status.unknown")}</span>
                ),
            }}
            i18nKey={mark ? "Edit <type></type> Mark" : "Add <type></type> Mark"}
          />
          {pathCount > 1 && (
            <span className="text-default-400 text-sm ml-2">
              ({t("pathMarkConfig.label.pathCount", { count: pathCount })})
            </span>
          )}
          <HelpCenterButton section="examples" topic="pathMark" />
        </span>
      }
      onDestroyed={onDestroyed}
      onOk={handleSave}
    >
      <div className="py-3">
        <div className="mb-4 rounded-xl border border-default-200 bg-content1 px-4 py-3">
          <div className="mb-1 text-[11px] font-medium uppercase tracking-wider text-default-400">
            {t("pathMarkConfig.layout.markPath")}
          </div>
          <div className="max-h-20 overflow-y-auto">
            {paths.map((path) => (
              <div key={path} className="break-all font-mono text-xs text-default-700">
                {path}
              </div>
            ))}
          </div>
        </div>
        <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,1.12fr)_minmax(0,.88fr)]">
          <div className="flex min-w-0 flex-col gap-4">
            <ConfigSection
              description={t(
                isResource
                  ? "pathMark.resource.explanation"
                  : "pathMarkConfig.layout.matchDescription",
              )}
              number="01"
              title={t("pathMarkConfig.layout.matchTitle")}
            >
              <div className="flex flex-col gap-4">
                <MatchModeSelector
                  config={config}
                  markType={markType}
                  t={t}
                  updateConfig={updateConfig}
                />
              </div>
            </ConfigSection>
            <ConfigSection
              description={t(
                isResource
                  ? "pathMarkConfig.layout.filtersDescription"
                  : "pathMarkConfig.layout.valueDescription",
              )}
              number="02"
              title={t(
                isResource
                  ? "pathMarkConfig.label.resourceFilters"
                  : "pathMarkConfig.layout.valueTitle",
              )}
            >
              {isResource ? (
                <ResourceMarkConfig config={config} t={t} updateConfig={updateConfig} />
              ) : markType === PathMarkType.Property ? (
                <PropertyMarkConfig config={config} t={t} updateConfig={updateConfig} />
              ) : (
                <MediaLibraryMarkConfig config={config} t={t} updateConfig={updateConfig} />
              )}
            </ConfigSection>
            <details className="rounded-2xl border border-default-200 bg-content1 p-4">
              <summary className="cursor-pointer text-sm font-medium text-default-600">
                {t("pathMarkConfig.layout.advanced")}
              </summary>
              <div className="mt-4 flex flex-col gap-4">
                <NumberInput
                  description={t("pathMark.priority.description")}
                  label={t("common.label.priority")}
                  size="sm"
                  value={priority}
                  onValueChange={(value) => setPriority(value ?? 10)}
                />
                {isResource && (
                  <Switch
                    isSelected={config.isResourceBoundary ?? false}
                    size="sm"
                    onValueChange={(value) => updateConfig({ isResourceBoundary: value })}
                  >
                    <span className="text-sm">{t("pathMark.resource.isResourceBoundary")}</span>
                  </Switch>
                )}
                {isResource && (
                  <p className="text-xs text-default-500">
                    {t("pathMark.resource.isResourceBoundary.description")}
                  </p>
                )}
                <Switch
                  isSelected={enableExpiration}
                  size="sm"
                  onValueChange={(value) => {
                    setEnableExpiration(value);
                    if (value && expiresInSeconds <= 0)
                      setExpiresInSeconds(DEFAULT_EXPIRES_IN_SECONDS);
                  }}
                >
                  {t("pathMarkConfig.label.scheduledSync")}
                </Switch>
                <p className="text-xs leading-relaxed text-default-500">
                  {t("pathMarkConfig.tip.expirationDescription")}
                </p>
                {enableExpiration && (
                  <DurationInput
                    minValue={60}
                    size="sm"
                    value={expiresInSeconds}
                    onChange={setExpiresInSeconds}
                  />
                )}
              </div>
            </details>
          </div>
          <aside className="min-w-0 lg:sticky lg:top-0">
            <ConfigSection
              description={t(
                isResource
                  ? "pathMarkConfig.layout.resourcePreviewDescription"
                  : "pathMarkConfig.layout.valuePreviewDescription",
              )}
              number="03"
              title={t("pathMarkConfig.layout.previewTitle")}
            >
              <PreviewResults
                {...preview}
                error={
                  !hasValidValueExtractor ? t("pathMarkConfig.value.fixValidation") : preview.error
                }
                markType={markType}
                t={t}
              />
            </ConfigSection>
          </aside>
        </div>
      </div>
    </Modal>
  );
};

export default MarkConfigModal;
