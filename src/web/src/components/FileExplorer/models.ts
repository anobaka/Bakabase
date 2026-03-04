type CapabilityDefinition = {
  shortcut?: {
    nameI18nKey: string;
    modifiers?: string[];
    key?: string;
    mouseButton?: number;
  };
  nameI18NKey: string;
};

export type Capability =
  | "select"
  | "multi-select"
  | "range-select"
  | "wrap"
  | "extract"
  | "move"
  | "delete"
  | "rename"
  | "decompress"
  | "delete-all-same-name"
  | "group"
  | "play"
  | "play-first-file"
  | "create-directory";

export const FileSystemTreeEntryCapabilityMap: Record<Capability, CapabilityDefinition> = {
  select: {
    shortcut: { nameI18nKey: "fileExplorer.shortcutKey.leftClick", mouseButton: 0 },
    nameI18NKey: "fileExplorer.capability.select",
  },
  "multi-select": {
    shortcut: { nameI18nKey: "fileExplorer.shortcutKey.ctrlClick", mouseButton: 0, modifiers: ["Control"] },
    nameI18NKey: "fileExplorer.capability.multiSelect",
  },
  "range-select": {
    shortcut: { nameI18nKey: "fileExplorer.shortcutKey.shiftClick", mouseButton: 0, modifiers: ["Shift"] },
    nameI18NKey: "fileExplorer.capability.rangeSelect",
  },
  wrap: {
    shortcut: { key: "w", nameI18nKey: "fileExplorer.shortcutKey.w" },
    nameI18NKey: "fileExplorer.capability.wrap",
  },
  extract: {
    shortcut: { key: "e", nameI18nKey: "fileExplorer.shortcutKey.e" },
    nameI18NKey: "fileExplorer.capability.extract",
  },
  move: {
    shortcut: { key: "m", nameI18nKey: "fileExplorer.shortcutKey.m" },
    nameI18NKey: "fileExplorer.capability.move",
  },
  delete: {
    shortcut: { key: "Delete", nameI18nKey: "fileExplorer.shortcutKey.delete" },
    nameI18NKey: "fileExplorer.capability.delete",
  },
  rename: {
    shortcut: { key: "F2", nameI18nKey: "fileExplorer.shortcutKey.f2" },
    nameI18NKey: "fileExplorer.capability.rename",
  },
  decompress: {
    shortcut: { key: "d", nameI18nKey: "fileExplorer.shortcutKey.d" },
    nameI18NKey: "fileExplorer.capability.decompress",
  },
  "delete-all-same-name": {
    nameI18NKey: "fileExplorer.capability.deleteAllSameName",
  },
  group: {
    shortcut: { key: "g", nameI18nKey: "fileExplorer.shortcutKey.g" },
    nameI18NKey: "fileExplorer.capability.group",
  },
  play: {
    nameI18NKey: "fileExplorer.capability.play",
  },
  "play-first-file": {
    shortcut: { key: " ", nameI18nKey: "fileExplorer.shortcutKey.space" },
    nameI18NKey: "fileExplorer.capability.playFirstFile",
  },
  "create-directory": {
    shortcut: { key: "n", nameI18nKey: "fileExplorer.shortcutKey.n" },
    nameI18NKey: "fileExplorer.capability.createDirectory",
  },
};
