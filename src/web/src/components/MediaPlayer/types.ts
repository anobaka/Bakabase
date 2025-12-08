import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry } from "@/sdk/Api";
import type { MediaType } from "@/sdk/constants";

// Separator used for paths inside compressed files (matches backend InternalOptions.CompressedFileRootSeparator)
export const COMPRESSED_FILE_ROOT_SEPARATOR = "!";

export interface MediaPlayerEntry
  extends BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry {
  // The full path to use for playing this file (includes compressed file path with separator if inside an archive)
  playPath?: string;
}

export interface MediaPlayerProps extends React.HTMLAttributes<HTMLElement> {
  defaultActiveIndex?: number;
  entries: BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry[];
  interval?: number;
  autoPlay?: boolean;
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
