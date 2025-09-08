type CapabilityDefinition = {
  shortcut?: {
    nameI18nKey: string,
    modifiers?: string[],
    key?: string,
    mouseButton?: number,
  },
  nameI18NKey: string,
}

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
  | "play-first-file";

export const FileSystemTreeEntryCapabilityMap: Record<Capability, CapabilityDefinition> = {
  select: {
    shortcut: { nameI18nKey: "Shortcut.LeftClick", mouseButton: 0 },
    nameI18NKey: "Capability.select"
  },
  "multi-select": {
    shortcut: { nameI18nKey: "Shortcut.CtrlClick", mouseButton: 0, modifiers: ["Control"] },
    nameI18NKey: "Capability.multi-select"
  },
  "range-select": {
    shortcut: { nameI18nKey: "Shortcut.ShiftClick", mouseButton: 0, modifiers: ["Shift"] },
    nameI18NKey: "Capability.range-select"
  },
  wrap: {
    shortcut: { key: "w", nameI18nKey: "Shortcut.W" },
    nameI18NKey: "Capability.wrap"
  },
  extract: {
    shortcut: { key: "e", nameI18nKey: "Shortcut.E" },
    nameI18NKey: "Capability.extract"
  },
  move: {
    shortcut: { key: "m", nameI18nKey: "Shortcut.M" },
    nameI18NKey: "Capability.move"
  },
  delete: {
    shortcut: { key: "Delete", nameI18nKey: "Shortcut.Delete" },
    nameI18NKey: "Capability.delete"
  },
  rename: {
    shortcut: { key: "F2", nameI18nKey: "Shortcut.F2" },
    nameI18NKey: "Capability.rename"
  },
  decompress: {
    shortcut: { key: "d", nameI18nKey: "Shortcut.D" },
    nameI18NKey: "Capability.decompress"
  },
  "delete-all-same-name": {
    nameI18NKey: "Capability.delete-all-same-name"
  },
  group: {
    shortcut: { key: "g", nameI18nKey: "Shortcut.G" },
    nameI18NKey: "Capability.group",
  },
  play: {
    nameI18NKey: "Capability.play"
  },
  "play-first-file": {
    shortcut: { key: " ", nameI18nKey: "Shortcut.Space" },
    nameI18NKey: "Capability.play-first-file",
  }
}
