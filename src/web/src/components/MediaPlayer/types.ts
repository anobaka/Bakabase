import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";
import type { MediaType } from "@/sdk/constants";

// Separator used for paths inside compressed files (matches backend InternalOptions.CompressedFileRootSeparator)
export const COMPRESSED_FILE_ROOT_SEPARATOR = "!";

export interface MediaPlayerEntry
  extends BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry {
  // The full path to use for playing this file (includes compressed file path with separator if inside an archive)
  playPath?: string;
}

/** A result of rendering delivered bytes, not a codec prediction based on a suffix. */
export type MediaPreviewState = "unknown" | "loading" | "ready" | "failed" | "unavailable";
export type MediaBrowseScope = "resource" | "configured";

export interface MediaPlayerProps extends React.HTMLAttributes<HTMLElement> {
  defaultActiveIndex?: number;
  entries: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry[];
  interval?: number;
  autoPlay?: boolean;
  /** Omitted for callers opening arbitrary selections rather than an entire resource. */
  resourceScope?: boolean;
  initialScope?: MediaBrowseScope;
  configuredPaths?: string[];
  /** Supplied by the resource action owner, which knows the viewing device's launch capability. */
  onOpenAlternative?: (filePath: string) => void | Promise<void>;
  canOpenAlternative?: (filePath: string) => boolean;
  renderOperations?: (
    filePath: string,
    mediaType: MediaType,
    playing: boolean,
    reactPlayer: any,
    image: HTMLImageElement | null,
  ) => any;
}

export interface MediaPlayerRef {
  gotoPrevEntry: () => void;
  gotoNextEntry: () => void;
}
