import { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { Checkbox, Input } from "@/components/bakaui";
import { bytesToSize } from "@/components/utils";
import type { AcquisitionDeletionPreview } from "./acquisitionDeletionApi";
import { deletionReasonLabel } from "./deletionReasonLabel";

type Directory = { taskId: number; path: string; eligible: boolean; files: number; bytes: number; sizeUnknown: boolean; registeredFilesOnly: boolean; reason?: string };
export type CleanupDirectorySelection = Record<string, string[]>;

export default function CleanupDirectoryPicker({ preview, selected, onChange }: {
  preview: AcquisitionDeletionPreview;
  selected: CleanupDirectorySelection;
  onChange: (value: CleanupDirectorySelection) => void;
}) {
  const { t } = useTranslation();
  const [query, setQuery] = useState("");
  const rows = useMemo(() => preview.rows.map(row => {
    const byPath = new Map<string, Directory>();
    for (const group of row.cleanup?.groups ?? []) {
      if (!group.directory) continue;
      const item = byPath.get(group.directory) ?? {
        taskId: row.taskId, path: group.directory, eligible: row.canDelete, files: 0, bytes: 0, sizeUnknown: false, registeredFilesOnly: true, reason: row.reasonCode,
      };
      item.eligible = item.eligible && group.eligible;
      item.reason ??= group.reasonCode;
      item.files += group.fileCount;
      item.bytes += group.bytes;
      item.sizeUnknown ||= group.sizeUnknown;
      item.registeredFilesOnly &&= group.registeredFilesOnly === true;
      byPath.set(group.directory, item);
    }
    return { row, directories: [...byPath.values()] };
  }), [preview]);
  const needle = query.trim().toLowerCase();
  const matches = rows.map(({ row, directories }) => ({
    row, directories: directories.filter(d => d.path.toLowerCase().includes(needle)),
  })).filter(r => r.directories.length > 0);
  const available = matches.flatMap(r => r.directories).filter(d => d.eligible);
  const isSelected = (d: Directory) => selected[d.taskId]?.includes(d.path) ?? false;
  const selectedMatches = available.filter(isSelected).length;
  const change = (items: Directory[], checked: boolean) => {
    const next = { ...selected };
    const grouped = new Map<number, Set<string>>();
    for (const d of items) {
      const paths = grouped.get(d.taskId) ?? new Set(selected[d.taskId]);
      if (checked) paths.add(d.path);
      else paths.delete(d.path);
      grouped.set(d.taskId, paths);
    }
    for (const [id, paths] of grouped) {
      if (paths.size) next[id] = [...paths];
      else delete next[id];
    }
    onChange(next);
  };
  return <div className="space-y-2">
    <Input size="sm" label={t("acquisition.delete.directories.filter")} value={query} onValueChange={setQuery} />
    <Checkbox size="sm" isSelected={available.length > 0 && selectedMatches === available.length}
      isIndeterminate={selectedMatches > 0 && selectedMatches < available.length} isDisabled={!available.length}
      onValueChange={checked => change(available, checked)}>{t("acquisition.delete.directories.selectMatches")}</Checkbox>
    <ul className="max-h-72 overflow-auto divide-y divide-default-200">
      {matches.map(({ row, directories }) => <li key={row.taskId} className="space-y-1 py-2">
        <p className="font-medium">#{row.taskId} {row.title}</p>
        {directories.map(d => <div key={d.path}>
          <Checkbox size="sm" isSelected={isSelected(d)} isDisabled={!d.eligible}
            onValueChange={checked => change([d], checked)}>
            <span className="break-all">{d.path}</span>
          </Checkbox>
          <p className="ms-7 text-xs text-default-500">{d.eligible ? t(d.sizeUnknown
            ? "acquisition.delete.directories.sizeUnknown" : "acquisition.delete.directories.size", {
              count: d.files, size: bytesToSize(d.bytes),
            }) : deletionReasonLabel(t, d.reason)}</p>
          {d.eligible && d.registeredFilesOnly && <p className="ms-7 text-xs text-default-500">
            {t("acquisition.delete.directories.registeredFilesOnly")}
          </p>}
        </div>)}
      </li>)}
    </ul>
    {!matches.length && <p>{t("acquisition.delete.directories.noMatches")}</p>}
    <p role="status">{t("acquisition.delete.directories.selected", { count: Object.values(selected).reduce((n, paths) => n + paths.length, 0) })}</p>
  </div>;
}
