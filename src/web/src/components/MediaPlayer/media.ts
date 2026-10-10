import type { BakabaseInsideWorldBusinessComponentsFileExplorerIwFsEntry as Entry } from "@/sdk/Api";
import { ExtensionMediaTypes, IwFsType, MediaType } from "@/sdk/constants";

export const getEntryMediaType = (entry: Entry): MediaType => {
  if (entry.type === IwFsType.Image) return MediaType.Image;
  if (entry.type === IwFsType.Video) return MediaType.Video;
  if (entry.type === IwFsType.Audio) return MediaType.Audio;
  const extension = (
    entry.ext?.replace(/^\./, "") ||
    entry.path.split(".").pop() ||
    ""
  ).toLowerCase();
  return ExtensionMediaTypes[`.${extension}`] ?? MediaType.Unknown;
};

export const isPreviewEntry = (entry: Entry) =>
  ![IwFsType.Directory, IwFsType.Drive, IwFsType.CompressedFileEntry].includes(entry.type);

const pathKey = (path: string) => {
  const normalized = path.replace(/\\/g, "/");
  return /^[a-z]:\//i.test(normalized) ? normalized.toLowerCase() : normalized;
};

/** Explicit selection wins, then the profile's located files, then useful media. */
export const selectInitialMediaIndex = (
  entries: Entry[],
  preferredPaths: string[] = [],
): number => {
  for (const preferred of preferredPaths) {
    const index = entries.findIndex((entry) => pathKey(entry.path) === pathKey(preferred));
    if (index >= 0) return index;
  }
  for (const type of [MediaType.Video, MediaType.Audio, MediaType.Image, MediaType.Text]) {
    const index = entries.findIndex((entry) => getEntryMediaType(entry) === type);
    if (index >= 0) return index;
  }
  return 0;
};

export const mediaTypeKey = (type: MediaType) =>
  ({
    [MediaType.Video]: "mediaPlayer.type.video",
    [MediaType.Audio]: "mediaPlayer.type.audio",
    [MediaType.Image]: "mediaPlayer.type.image",
    [MediaType.Text]: "mediaPlayer.type.text",
  })[type] ?? "mediaPlayer.type.file";
