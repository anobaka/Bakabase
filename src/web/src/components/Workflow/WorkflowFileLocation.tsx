import { lazy, Suspense, useEffect, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { CopyOutlined, FolderOpenOutlined } from "@ant-design/icons";
import { Button, Spinner, Tooltip } from "@/components/bakaui";
import FilePathValue from "@/components/FilePathValue";
import { copyTextToClipboard } from "@/core/clipboard";
import type { TaskDirectoryContext } from "./useTaskDirectoryAccess";

// The browser imports workflow actions; keep its bundle outside registry initialization.
const WorkflowArtifactBrowserModal = lazy(() => import("./WorkflowArtifactBrowserModal"));

/** Recorded locations may be inspected; planned or retired locations stay display-only. */
export default function WorkflowFileLocation({
  path,
  available,
  label,
  className = "",
  taskDirectory,
}: {
  path: string;
  available: boolean;
  label?: ReactNode;
  className?: string;
  taskDirectory?: TaskDirectoryContext;
}) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [copyError, setCopyError] = useState(false);
  useEffect(() => {
    setOpen(false);
    setCopyError(false);
  }, [path, available]);
  return (
    <div className={`min-w-0 space-y-2 ${className}`}>
      {label && <p className="text-xs text-default-500">{label}</p>}
      <div className="flex min-w-0 flex-wrap items-center gap-2">
        {available ? (
          <FilePathValue path={path} compact pathActions={
            <Tooltip content={t("workflow.location.browseFolder")}>
              <Button size="sm" variant="light" isIconOnly className="shrink-0"
                aria-label={t("workflow.location.browseFolder")} aria-haspopup="dialog"
                onPress={() => setOpen(true)}>
                <FolderOpenOutlined aria-hidden />
              </Button>
            </Tooltip>
          } />
        ) : (
          <div className="flex min-w-0 items-center gap-2">
            <code className="select-text break-all text-xs">{path}</code>
            <Button
              size="sm"
              variant="light"
              isIconOnly
              aria-label={t("workflow.location.copyPath")}
              onPress={() =>
                void copyTextToClipboard(path)
                  .then(() => setCopyError(false))
                  .catch(() => setCopyError(true))
              }
            >
              <CopyOutlined aria-hidden />
            </Button>
          </div>
        )}
      </div>
      {copyError && (
        <p role="alert" className="text-xs text-danger">
          {t("filePath.copyFailed")}
        </p>
      )}
      {available && open && (
        <Suspense fallback={<Spinner size="sm" />}>
          <WorkflowArtifactBrowserModal
            key={path}
            location={{ path, label }}
            taskDirectory={taskDirectory}
            onClose={() => setOpen(false)}
          />
        </Suspense>
      )}
    </div>
  );
}
