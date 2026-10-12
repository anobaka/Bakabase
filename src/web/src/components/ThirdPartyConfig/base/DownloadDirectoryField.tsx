"use client";

import type { ReactNode } from "react";

import { useId } from "react";
import { useTranslation } from "react-i18next";
import { FiFolder, FiX } from "react-icons/fi";

import { Button } from "@/components/bakaui";
import { FileSystemSelectorButton } from "@/components/FileSystemSelector";

type Props = {
  value?: string | null;
  label: string;
  placeholder?: string;
  description?: ReactNode;
  startPath?: string;
  clearLabel?: string;
  onChange: (path: string) => void;
};

/** Keep the stored choice separate from any inherited path shown in the description. */
export default function DownloadDirectoryField({
  value,
  label,
  placeholder,
  description,
  startPath,
  clearLabel,
  onChange,
}: Props) {
  const { t } = useTranslation();
  const labelId = useId();
  const descriptionId = useId();
  const configuredPath = value?.trim() ? value : undefined;
  const resetLabel = clearLabel ?? t("downloader.config.defaultDownloadPath.clear");

  return (
    <div className="flex min-w-0 flex-col gap-1.5">
      <span className="text-sm font-medium" id={labelId}>
        {label}
      </span>
      <div className="flex min-w-0 items-center gap-1.5">
        <FileSystemSelectorButton
          key={configuredPath ?? "unset"}
          aria-describedby={description ? descriptionId : undefined}
          aria-labelledby={labelId}
          className="min-w-0 max-w-full justify-start"
          fileSystemSelectorProps={{
            targetType: "folder",
            defaultSelectedPath: configuredPath,
            startPath: configuredPath ?? startPath,
            onSelected: (entry) => onChange(entry.path),
          }}
          startContent={<FiFolder aria-hidden className="shrink-0" />}
          variant="flat"
        >
          {placeholder}
        </FileSystemSelectorButton>
        {configuredPath && (
          <Button
            isIconOnly
            aria-label={resetLabel}
            className="shrink-0"
            size="sm"
            title={resetLabel}
            variant="light"
            onPress={() => onChange("")}
          >
            <FiX aria-hidden />
          </Button>
        )}
      </div>
      {description && (
        <div className="text-xs leading-relaxed text-default-500" id={descriptionId}>
          {description}
        </div>
      )}
    </div>
  );
}
