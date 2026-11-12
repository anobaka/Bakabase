import { Entry, EntryProperty } from "./Entry";
import { IwFsType } from "@/sdk/constants";

export interface DirectoryListing {
  entries: readonly { relativePath: string; isDirectory: boolean; length?: number | null }[];
  childCount: number;
}

/** Read-only entries supplied by the caller. Paths are relative keys in this one source. */
export interface ExplorerDirectoryPage {
  directory: string;
  entries: DirectoryListing["entries"];
  onNavigate: (directory: string) => void;
  loadChildren: (directory: string, signal: AbortSignal) => Promise<DirectoryListing>;
}

export function pageChildren(parent: Entry, entries: DirectoryListing["entries"]) {
  return entries.map(row => new Entry({
    parent,
    name: row.relativePath.slice(row.relativePath.lastIndexOf("/") + 1),
    path: row.relativePath,
    type: row.isDirectory ? IwFsType.Directory : IwFsType.Unknown,
    size: row.length ?? undefined,
    properties: [EntryProperty.Size],
  }));
}
