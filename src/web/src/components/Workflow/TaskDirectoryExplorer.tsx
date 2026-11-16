import { lazy, Suspense, useRef, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { ReloadOutlined } from "@ant-design/icons";
import { LuInfo } from "react-icons/lu";
import { Button, Spinner, Tooltip } from "@/components/bakaui";
import { managedFileCapabilities } from "@/components/FileExplorer/models";
import type { FileExplorerRef } from "@/components/FileExplorer";
import DirectoryTaskStatus from "@/components/FileExplorer/components/DirectoryTaskStatus";
import useTaskDirectoryAccess, { type TaskDirectoryContext } from "./useTaskDirectoryAccess";

const FileExplorer = lazy(() => import("@/components/FileExplorer/FileExplorer"));

export function isTaskDirectoryPath(root: string, target?: string) {
  if (!target) return false;
  const normalizedRoot = root.replace(/\\/g, "/").replace(/\/+$/, "");
  const normalizedTarget = target.replace(/\\/g, "/").replace(/\/+$/, "");
  if (normalizedTarget.split("/").some(part => part === "." || part === "..")) return false;
  return normalizedTarget === normalizedRoot || normalizedTarget.startsWith(`${normalizedRoot}/`);
}

/** The regular file processor, restricted to viewing until current task state is known. */
export default function TaskDirectoryExplorer({ path, context, maxContentHeight, heading }: {
  path: string; context: TaskDirectoryContext; maxContentHeight?: number; heading?: ReactNode;
}) {
  const { t } = useTranslation();
  const access = useTaskDirectoryAccess(context, path);
  const editable = access === "editable";
  const ref = useRef<FileExplorerRef>(null);
  return <div className={`flex min-h-0 min-w-0 flex-col gap-1 ${maxContentHeight ? "" : "flex-1"}`}>
    <div className="flex shrink-0 flex-wrap items-center justify-between gap-2 text-xs text-default-500">
      <div className="flex min-w-0 flex-wrap items-center gap-1">
        {heading}
        {heading ? <Tooltip placement="bottom" content={<span className="block max-w-sm whitespace-pre-wrap">{t(`workflow.files.taskDirectory.${access}`)}</span>}>
          <Button size="sm" variant="light" className="h-6 max-w-full px-1 text-xs text-default-500"
            startContent={<LuInfo aria-hidden size={13} />}>
            {t(`workflow.files.taskDirectory.compact.${access}`)}
          </Button>
        </Tooltip> : <p role="status">{t(`workflow.files.taskDirectory.${access}`)}</p>}
        {(editable || access === "active") && <DirectoryTaskStatus path={path} />}
      </div>
      <Button size="sm" variant="light" startContent={<ReloadOutlined aria-hidden />}
        onPress={() => void ref.current?.root?.expand(true)}>{t("workflow.files.taskDirectory.refresh")}</Button>
    </div>
    <Suspense fallback={<Spinner size="sm" />}>
      <FileExplorer key={path} ref={ref} rootPath={path} maxContentHeight={maxContentHeight}
        canNavigateToPath={target => isTaskDirectoryPath(path, target)}
        expandable capabilities={editable ? managedFileCapabilities : ["enter-directory"]}
        keyboard={editable} watchChanges={editable || access === "active" ? "poll" : false}
        selectable={editable ? "multiple" : "disabled"} />
    </Suspense>
  </div>;
}
