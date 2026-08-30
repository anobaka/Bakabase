"use client";

import { useTranslation } from "react-i18next";
import { AiOutlineFile, AiOutlineFolder } from "react-icons/ai";

export type TreeMarkType = "resource" | "property" | "mediaLibrary";

export interface TreeLine {
  depth: number;
  /** i18n key under helpCenter.pathMark.node.* for localizable folder names. */
  nameKey?: string;
  /** Literal name (file names such as movie.mkv that need no translation). */
  literal?: string;
  kind: "dir" | "file";
  /** Highlights the line with the mark type's color and shows a badge. */
  mark?: TreeMarkType;
  /** i18n key of the badge text; defaults to the mark type name. */
  badgeKey?: string;
  /** Dim the line (context-only entries such as "…"). */
  muted?: boolean;
}

const markStyles: Record<TreeMarkType, { line: string; badge: string }> = {
  resource: {
    line: "bg-success/10",
    badge: "bg-success/15 text-success",
  },
  property: {
    line: "bg-primary/10",
    badge: "bg-primary/15 text-primary",
  },
  mediaLibrary: {
    line: "bg-secondary/10",
    badge: "bg-secondary/15 text-secondary",
  },
};

const defaultBadgeKeys: Record<TreeMarkType, string> = {
  resource: "helpCenter.pathMark.badge.resource",
  property: "helpCenter.pathMark.badge.property",
  mediaLibrary: "helpCenter.pathMark.badge.mediaLibrary",
};

/**
 * Renders a sample directory tree with mark highlights. Shared by the
 * "what is it" diagram and every example card so trees look identical
 * everywhere.
 */
const DirectoryTree = ({ lines, className }: { lines: TreeLine[]; className?: string }) => {
  const { t } = useTranslation();

  return (
    <div
      className={`flex flex-col gap-0.5 rounded-lg bg-default-50 border border-default-200 p-2 text-sm font-mono ${className ?? ""}`}
    >
      {lines.map((line, index) => {
        const style = line.mark ? markStyles[line.mark] : undefined;
        const name = line.nameKey ? t(line.nameKey) : (line.literal ?? "");
        const badgeText = line.mark
          ? t(line.badgeKey ?? defaultBadgeKeys[line.mark])
          : line.badgeKey
            ? t(line.badgeKey)
            : undefined;

        return (
          <div
            key={index}
            className={`flex items-center gap-1.5 rounded px-1.5 py-0.5 min-w-0 ${style?.line ?? ""} ${
              line.muted ? "opacity-50" : ""
            }`}
            style={{ marginLeft: line.depth * 18 }}
          >
            {line.kind === "dir" ? (
              <AiOutlineFolder className="shrink-0 text-warning" />
            ) : (
              <AiOutlineFile className="shrink-0 text-default-400" />
            )}
            <span className="truncate text-default-700">{name}</span>
            {badgeText && (
              <span
                className={`shrink-0 rounded px-1.5 py-0.5 text-xs font-sans ${
                  style?.badge ?? "bg-default-200 text-default-600"
                }`}
              >
                {badgeText}
              </span>
            )}
          </div>
        );
      })}
    </div>
  );
};

DirectoryTree.displayName = "DirectoryTree";

export default DirectoryTree;
