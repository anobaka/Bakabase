import { archiveOpenModes, type ArchiveOpenMode } from "@/sdk/constants";

export function isArchiveOpenMode(value: unknown): value is ArchiveOpenMode {
  return archiveOpenModes.some((choice) => choice.value === value);
}

/** Form completeness only; the server validates the actual container and member catalogue. */
export function isCompleteArchiveOpening(step: Record<string, unknown>): boolean {
  if (step.archiveOpenMode == null) return step.archiveMemberSelector == null;
  if (step.op !== "extractArchive" || !isArchiveOpenMode(step.archiveOpenMode)) return false;
  if (step.zipFileNameCodePage != null || step.zipPasswordCodePage != null) return false;
  const source = step.selector;
  if (typeof source !== "string" || !source.trim() || /[*?]/.test(source)) return false;
  const member = step.archiveMemberSelector;
  return (
    member == null ||
    (typeof member === "string" &&
      member.trim().length > 0 &&
      member.length <= 255 &&
      member !== "." &&
      member !== ".." &&
      !/[\\/:*?<>"|\x00-\x1f\x7f-\x9f]/.test(member) &&
      !/[. ]$/.test(member) &&
      !/^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)/i.test(member))
  );
}
