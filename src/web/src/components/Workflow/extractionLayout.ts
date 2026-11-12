export type ExtractionLayout = "currentDirectory" | "archiveDirectory";

export function isExtractionLayout(value: unknown): value is ExtractionLayout {
  return value === "currentDirectory" || value === "archiveDirectory";
}
