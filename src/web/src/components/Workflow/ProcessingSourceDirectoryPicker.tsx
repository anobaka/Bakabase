import { useTranslation } from "react-i18next";
import FileSystemSelectorButton from "@/components/FileSystemSelector/components/Button";
import { isCompleteDirectoryRebase } from "./processingPlanEditor";

/** The server accepts a directory relative to this exact input, on either host OS. */
export function relativeInputDirectory(root: string, path: string): string | undefined {
  const base = root.replace(/\\/g, "/").replace(/\/+$/, "") + "/";
  const child = path.replace(/\\/g, "/").replace(/\/+$/, "");
  const windows = /^[A-Za-z]:\//.test(base) || base.startsWith("//");
  const prefixMatches = windows
    ? child.toLowerCase().startsWith(base.toLowerCase())
    : child.startsWith(base);
  if (!prefixMatches) return undefined;
  const relative = child.slice(base.length);
  return isCompleteDirectoryRebase({ sourceDirectory: relative, targetDirectory: "" })
    ? relative
    : undefined;
}

export default function ProcessingSourceDirectoryPicker({
  root,
  disabled,
  onSelect,
}: {
  root: string;
  disabled: boolean;
  onSelect: (relative: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <FileSystemSelectorButton
      isDisabled={disabled}
      fileSystemSelectorProps={{
        targetType: "folder",
        startPath: root,
        capabilities: ["select", "enter-directory"],
        filter: (entry) => !!entry.path && relativeInputDirectory(root, entry.path) !== undefined,
        onSelected: (entry) => {
          const relative = entry.path && relativeInputDirectory(root, entry.path);
          if (relative) onSelect(relative);
        },
      }}
    >
      {t("workflow.processing.editor.chooseSourceDirectory")}
    </FileSystemSelectorButton>
  );
}
