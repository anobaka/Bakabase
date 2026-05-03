"use client";

import React, { useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineDown, AiOutlineRight } from "react-icons/ai";

import { Spinner } from "@/components/bakaui";
import FileSystemEntryIcon from "@/components/FileSystemEntryIcon";
import { IconType } from "@/sdk/constants";

export type FileChangeHighlightSpan = { start: number; length: number };

export type FileChangeEntry = {
  name: string;
  isDirectory: boolean;
  highlightSpans?: FileChangeHighlightSpan[];
};

export type FileChangeGroup = {
  targetName: string;
  targetIsDirectory: boolean;
  members: FileChangeEntry[];
};

export type FileChangeBatch = {
  rootPath: string;
  groups: FileChangeGroup[];
  untouched?: FileChangeEntry[];
};

type Props = {
  batches: FileChangeBatch[];
  isLoading?: boolean;
  emptyText?: string;
  collapsedMemberPreviewCount?: number;
  largeGroupThreshold?: number;
  className?: string;
};

const renderHighlightedName = (
  entry: FileChangeEntry,
): React.ReactNode => {
  const spans = (entry.highlightSpans ?? [])
    .filter((s) => s.length > 0 && s.start >= 0 && s.start < entry.name.length)
    .slice()
    .sort((a, b) => a.start - b.start);

  if (spans.length === 0) return entry.name;

  const merged: FileChangeHighlightSpan[] = [];
  for (const s of spans) {
    const last = merged[merged.length - 1];
    if (last && s.start <= last.start + last.length) {
      const end = Math.max(last.start + last.length, s.start + s.length);
      last.length = end - last.start;
    } else {
      merged.push({ start: s.start, length: s.length });
    }
  }

  const out: React.ReactNode[] = [];
  let cursor = 0;
  merged.forEach((s, i) => {
    if (cursor < s.start) {
      out.push(<span key={`p-${i}`}>{entry.name.slice(cursor, s.start)}</span>);
    }
    out.push(
      <span
        key={`h-${i}`}
        className="bg-yellow-200/50 dark:bg-yellow-500/30 rounded-sm px-0.5"
      >
        {entry.name.slice(s.start, s.start + s.length)}
      </span>,
    );
    cursor = s.start + s.length;
  });
  if (cursor < entry.name.length) {
    out.push(<span key="tail">{entry.name.slice(cursor)}</span>);
  }
  return <>{out}</>;
};

const EntryRow = ({
  entry,
  layer,
  muted,
}: {
  entry: FileChangeEntry;
  layer: number;
  muted?: boolean;
}) => (
  <div
    className={`flex items-center gap-2 ${muted ? "opacity-60" : ""}`}
    style={{ paddingLeft: `${layer * 24}px` }}
  >
    <FileSystemEntryIcon
      size={18}
      type={entry.isDirectory ? IconType.Directory : IconType.UnknownFile}
    />
    <span className="text-sm whitespace-break-spaces">
      {renderHighlightedName(entry)}
    </span>
  </div>
);

const GroupBlock = ({
  group,
  collapsedMemberPreviewCount,
  largeGroupThreshold,
}: {
  group: FileChangeGroup;
  collapsedMemberPreviewCount: number;
  largeGroupThreshold: number;
}) => {
  const { t } = useTranslation();
  const isLarge = group.members.length > largeGroupThreshold;
  const [collapsed, setCollapsed] = useState(isLarge);

  const visibleMembers = collapsed
    ? group.members.slice(0, collapsedMemberPreviewCount)
    : group.members;
  const hiddenCount = group.members.length - visibleMembers.length;

  return (
    <div className="flex flex-col gap-0.5">
      <button
        type="button"
        className="flex items-center gap-2 text-left"
        style={{ paddingLeft: `24px` }}
        onClick={() => setCollapsed((c) => !c)}
      >
        {collapsed ? (
          <AiOutlineRight className="text-xs opacity-60" />
        ) : (
          <AiOutlineDown className="text-xs opacity-60" />
        )}
        <FileSystemEntryIcon size={18} type={IconType.Directory} />
        <span className="text-sm font-medium">{group.targetName}</span>
        <span className="text-xs opacity-60">({group.members.length})</span>
      </button>
      {visibleMembers.map((m) => (
        <EntryRow key={m.name} entry={m} layer={2} />
      ))}
      {hiddenCount > 0 && (
        <button
          type="button"
          className="text-xs opacity-70 hover:opacity-100 text-left"
          style={{ paddingLeft: `${2 * 24}px` }}
          onClick={() => setCollapsed(false)}
        >
          + {t<string>("FileChangePreview.MoreEntries", { count: hiddenCount })}
        </button>
      )}
    </div>
  );
};

const UntouchedBlock = ({ entries }: { entries: FileChangeEntry[] }) => {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);

  if (entries.length === 0) return null;

  return (
    <div className="flex flex-col gap-0.5 mt-1">
      <button
        type="button"
        className="flex items-center gap-2 text-left text-sm opacity-70 hover:opacity-100"
        style={{ paddingLeft: `24px` }}
        onClick={() => setOpen((o) => !o)}
      >
        {open ? (
          <AiOutlineDown className="text-xs" />
        ) : (
          <AiOutlineRight className="text-xs" />
        )}
        <span>
          {t<string>("FileChangePreview.UntouchedCount", { count: entries.length })}
        </span>
      </button>
      {open &&
        entries.map((e) => (
          <EntryRow key={e.name} entry={e} layer={2} muted />
        ))}
    </div>
  );
};

const FileChangePreview = ({
  batches,
  isLoading,
  emptyText,
  collapsedMemberPreviewCount = 3,
  largeGroupThreshold = 8,
  className,
}: Props) => {
  const { t } = useTranslation();

  const summary = useMemo(() => {
    let groupCount = 0;
    let movingCount = 0;
    let untouchedCount = 0;
    for (const b of batches) {
      groupCount += b.groups.length;
      for (const g of b.groups) {
        movingCount += g.members.length;
      }
      untouchedCount += b.untouched?.length ?? 0;
    }
    return { groupCount, movingCount, untouchedCount };
  }, [batches]);

  if (isLoading) {
    return (
      <div className="flex items-center gap-2 py-4">
        <Spinner size="sm" />
        <span className="text-sm opacity-70">
          {t<string>("FileChangePreview.Calculating")}
        </span>
      </div>
    );
  }

  const hasAnyGroups = batches.some((b) => b.groups.length > 0);

  if (!hasAnyGroups) {
    return (
      <div className="text-sm opacity-70 py-4 text-center">
        {emptyText ?? t<string>("FileChangePreview.NothingToChange")}
      </div>
    );
  }

  return (
    <div
      className={`flex flex-col gap-2 overflow-auto ${
        className ?? "max-h-[60vh]"
      }`}
    >
      <div className="sticky top-0 z-10 bg-content1 border-b border-default-200 px-2 py-1.5 text-xs flex items-center gap-3">
        <span>
          <strong>{summary.groupCount}</strong>{" "}
          {t<string>("FileChangePreview.SummaryGroups")}
        </span>
        <span className="opacity-60">·</span>
        <span>
          <strong>{summary.movingCount}</strong>{" "}
          {t<string>("FileChangePreview.SummaryMoving")}
        </span>
        {summary.untouchedCount > 0 && (
          <>
            <span className="opacity-60">·</span>
            <span className="opacity-70">
              <strong>{summary.untouchedCount}</strong>{" "}
              {t<string>("FileChangePreview.SummaryUntouched")}
            </span>
          </>
        )}
      </div>
      {batches.map((batch) => (
        <div key={batch.rootPath} className="flex flex-col gap-0.5">
          <div className="flex items-center gap-2">
            <FileSystemEntryIcon size={20} type={IconType.Directory} />
            <span className="text-sm font-medium">{batch.rootPath}</span>
          </div>
          {batch.groups.length === 0 ? (
            <div
              className="text-xs opacity-60"
              style={{ paddingLeft: `24px` }}
            >
              {t<string>("FileChangePreview.NothingToChange")}
            </div>
          ) : (
            batch.groups.map((g) => (
              <GroupBlock
                key={g.targetName}
                group={g}
                collapsedMemberPreviewCount={collapsedMemberPreviewCount}
                largeGroupThreshold={largeGroupThreshold}
              />
            ))
          )}
          {batch.untouched && batch.untouched.length > 0 && (
            <UntouchedBlock entries={batch.untouched} />
          )}
        </div>
      ))}
    </div>
  );
};

FileChangePreview.displayName = "FileChangePreview";

export default FileChangePreview;
