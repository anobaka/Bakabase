"use client";

import React, { useEffect, useState } from "react";
import { CheckOutlined, CopyOutlined, FolderOpenOutlined } from "@ant-design/icons";
import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { ClientMode } from "@/sdk/constants";
import { copyTextToClipboard } from "@/core/clipboard";
import { useDeploymentPathsStore } from "@/stores/deploymentPaths";
import { useRemoteAccessStore, useUserSideActionsRunHere } from "@/stores/remoteAccess";

interface Props {
  path?: string;
  /** Small muted line under the path, explaining what lives there. */
  description?: React.ReactNode;
  size?: "sm" | "md" | "lg";
  className?: string;
}

/** Host paths are display-only. File operations always retain the original server path. */
const FilePathValue: React.FC<Props> = ({ path, description, size = "sm", className }) => {
  const { t } = useTranslation();
  const canRunNativeAction = useUserSideActionsRunHere();
  const clientMode = useRemoteAccessStore((state) => state.clientMode);
  const initialized = useRemoteAccessStore((state) => state.initialized);
  const deployment = useDeploymentPathsStore((state) => state.data);
  const load = useDeploymentPathsStore((state) => state.load);
  const [copiedPath, setCopiedPath] = useState<string>();
  const [copyFailed, setCopyFailed] = useState(false);

  useEffect(() => {
    if (path && initialized && clientMode !== ClientMode.AllInOne) void load();
  }, [path, initialized, clientMode, load]);

  if (!path) {
    return null;
  }

  const entry = deployment?.paths?.find((entry) => entry.serverPath === path);
  const hostPath = entry?.hostPath || undefined;
  const isContainer = deployment?.isContainer === true;
  const label = hostPath
    ? "filePath.hostPath"
    : isContainer
      ? "filePath.containerPath"
      : clientMode !== ClientMode.AllInOne
        ? "filePath.serverPath"
        : undefined;

  const copy = async (value: string) => {
    try {
      await copyTextToClipboard(value);
      setCopiedPath(value);
      setCopyFailed(false);
    } catch {
      setCopiedPath(undefined);
      setCopyFailed(true);
    }
  };

  const renderCopyable = (value: string, pathLabel?: string) => (
    <div className="inline-flex max-w-full min-w-0 items-center gap-2 rounded-lg border border-default-200 px-2 py-1">
      <code className="min-w-0 select-text break-all text-xs text-foreground-700">{value}</code>
      <Button
        isIconOnly
        aria-label={t(copiedPath === value ? "filePath.copied" : "filePath.copy", {
          label: pathLabel ? t(pathLabel) : t("filePath.path"),
        })}
        className="shrink-0"
        size={size}
        variant="light"
        onPress={() => void copy(value)}
      >
        {copiedPath === value ? <CheckOutlined aria-hidden /> : <CopyOutlined aria-hidden />}
      </Button>
    </div>
  );

  return (
    <div className={`flex min-w-0 flex-col items-start gap-1 ${className ?? ""}`}>
      {label && (
        <div className="flex flex-wrap gap-2 text-xs text-foreground-400">
          <span>{t(label)}</span>
          {entry?.readOnly && <span>{t("filePath.readOnlyMount")}</span>}
        </div>
      )}
      <div className="flex max-w-full min-w-0 items-center gap-1">
        {renderCopyable(hostPath || path, label)}
        {canRunNativeAction && (
          <Button
            isIconOnly
            aria-label={t("filePath.openFolder")}
            color="primary"
            size={size}
            variant="light"
            onPress={() => BApi.tool.openFileOrDirectory({ path })}
          >
            <FolderOpenOutlined aria-hidden className="text-base" />
          </Button>
        )}
      </div>
      {hostPath && (
        <details className="max-w-full text-xs text-foreground-400">
          <summary className="w-fit cursor-pointer">{t("filePath.containerPath")}</summary>
          <div className="mt-1">{renderCopyable(path, "filePath.containerPath")}</div>
        </details>
      )}
      {isContainer && !hostPath && (
        <span className="text-xs text-foreground-400">
          {t(
            entry?.storageKind === "volume"
              ? "filePath.dockerVolume"
              : entry?.storageKind === "container"
                ? "filePath.containerStorage"
                : "filePath.hostPathUnavailable",
          )}
        </span>
      )}
      {copyFailed && (
        <span className="text-xs text-danger" role="alert">
          {t("filePath.copyFailed")}
        </span>
      )}
      {description && <span className="text-xs text-foreground-400">{description}</span>}
    </div>
  );
};

FilePathValue.displayName = "FilePathValue";

export default FilePathValue;
