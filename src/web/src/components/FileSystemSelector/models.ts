import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { Capability } from "@/components/FileExplorer/models";

export type FileSystemSelectorProps = {
  /** The normal picker selects an entry; manager mode keeps the explorer open for file work. */
  mode?: "select" | "manage";
  startPath?: string;
  targetType?: "file" | "folder";
  capabilities?: Capability[];
  keyboard?: boolean;
  onSelected?: (entry: Entry) => any;
  onCancel?: () => any;
  filter?: (entry: Entry) => boolean;
  defaultSelectedPath?: string;
  multiple?: boolean;
  onMultipleSelected?: (entries: Entry[]) => any;
};
