import type { TFunction } from "i18next";
import type { BakabaseServiceComponentsCloudDownloadsRcloneRcloneConfigurationQuestionViewModel as Question } from "@/sdk/Api";

export type RcloneAnswerOption = {
  value: string;
  label: string;
  description?: string;
  originalHelp: string;
};

export const isRcloneDriveQuestion = (question: Pick<Question, "name">) =>
  ["config_driveid", "config_driveid_fixed"].includes(question.name);

const connectionTypes: Record<string, "common" | "sharepoint" | "advanced"> = {
  onedrive: "common",
  sharepoint: "sharepoint",
  url: "sharepoint",
  search: "sharepoint",
  driveid: "advanced",
  siteid: "advanced",
  path: "advanced",
};

function looksLikeIdentifier(name: string, value: string) {
  return (
    !name ||
    name === value ||
    /^b![A-Za-z0-9_-]+$/.test(name) ||
    /^\{?[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\}?$/i.test(name) ||
    /^[0-9a-f]{16,}$/i.test(name) ||
    (name.length >= 32 && /^[A-Za-z0-9_-]+$/.test(name) && /[A-Za-z]/.test(name) && /\d/.test(name))
  );
}

/** Presentation only: retain every distinct native value and original description.
 * rclone v1.75.2 emits drive examples as `${DriveName} (${DriveType})`.
 * Unknown questions/formats fall back to the native text; labels never decide eligibility.
 */
export function presentRcloneAnswerOptions(
  question: Pick<Question, "name" | "examples" | "isSecret">,
  t: TFunction,
): RcloneAnswerOption[] {
  if (question.isSecret) return [];
  // An upstream list can return the same drive by several routes. Exact IDs are
  // identity; names and drive types never merge different drives or decide access.
  const examples = isRcloneDriveQuestion(question)
    ? [
        ...question.examples
          .reduce((unique, example) => {
            const previous = unique.get(example.value);
            const readable = (help: string) => {
              const name =
                /^(.*?)\s*\((personal|business|documentLibrary|drive)\)\s*$/s
                  .exec(help)?.[1]
                  ?.trim() ?? help;
              return (
                !!name && !looksLikeIdentifier(name, example.value) && name !== "Chosen Drive ID"
              );
            };
            if (!previous || (!readable(previous.help) && readable(example.help)))
              unique.set(example.value, example);
            return unique;
          }, new Map<string, Question["examples"][number]>())
          .values(),
      ]
    : question.examples;
  const options = examples.map((example, index): RcloneAnswerOption => {
    const native = {
      value: example.value,
      label: example.help || example.value,
      originalHelp: example.help,
    };
    if (
      question.name === "config_type" &&
      Object.prototype.hasOwnProperty.call(connectionTypes, example.value)
    ) {
      const group = t(`rclone.authorization.connectionGroup.${connectionTypes[example.value]}`);
      return {
        ...native,
        label: `${group} · ${t(`rclone.authorization.connection.${example.value}.label`)}`,
        description: t(`rclone.authorization.connection.${example.value}.description`),
      };
    }
    if (question.name === "config_drive_ok" && ["true", "false"].includes(example.value)) {
      return {
        ...native,
        label: t(`rclone.authorization.confirmDrive.${example.value}`),
      };
    }
    if (!isRcloneDriveQuestion(question)) return native;
    const match = /^(.*?)\s*\((personal|business|documentLibrary|drive)\)\s*$/s.exec(example.help);
    if (!match) return native;
    const name = match[1].trim();
    const type = match[2] !== "drive" ? match[2] : "unknown";
    const typeLabel = t(`rclone.authorization.driveType.${type}`);
    const fallback = looksLikeIdentifier(name, example.value) || name === "Chosen Drive ID";
    return {
      ...native,
      label: fallback
        ? t("rclone.authorization.driveFallback", { type: typeLabel, number: index + 1 })
        : `${name} · ${typeLabel}`,
      description: fallback
        ? t("rclone.authorization.driveIdentifierHint")
        : t("rclone.authorization.driveAccessHint"),
    };
  });
  if (!isRcloneDriveQuestion(question)) return options;
  // Identically named drives stay distinct without exposing opaque IDs as the main label.
  const counts = new Map<string, number>();
  options.forEach(({ label }) => counts.set(label, (counts.get(label) ?? 0) + 1));
  return options.map((option, index) =>
    counts.get(option.label)! > 1 ? { ...option, label: `${option.label} (${index + 1})` } : option,
  );
}
