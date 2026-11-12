import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Button, Input } from "@/components/bakaui";
import FileSystemSelectorButton from "@/components/FileSystemSelector/components/Button";
import type { BakabaseModulesAcquisitionAbstractionsModelsDomainAcquisitionDeliverySettings as DeliverySettings } from "@/sdk/Api";
import {
  defaultDeliveryContentLayout,
  deliveryContentLayoutIsValid,
  deliveryContentLayoutsEqual,
} from "@/components/FileArtifacts/DeliveryContentLayoutFields";

export type { DeliverySettings };

export const defaultDeliverySettings = {
  rootDirectory: "",
  directoryTemplate: "{Title} [A{TaskId}]",
  partDirectoryTemplate: "{PartTitle} [{PartId}]",
};

export function deliveryTemplateIsInvalid(value: string, part = false) {
  const required = part ? "{PartId}" : "{TaskId}";
  const allowed = part ? ["PartTitle", "PartId"] : ["Title", "TaskId"];
  const normalized = value;
  const suffix = part ? "[{PartId}]" : "[A{TaskId}]";
  const leaf = normalized.split("/").at(-1) ?? "";
  if (
    normalized.length > 240 ||
    !leaf.endsWith(suffix) ||
    leaf.indexOf(required) !== leaf.lastIndexOf(required) ||
    normalized.includes("\\") ||
    normalized.startsWith("/")
  )
    return true;
  if (
    normalized
      .split("/")
      .some(
        (segment) =>
          !segment.trim() || [".", ".."].includes(segment.trim()) || /[ .]$/.test(segment),
      )
  )
    return true;
  const remaining = normalized.replace(/\{([A-Za-z]+)\}/g, (token, name: string) =>
    allowed.includes(name) ? "example" : token,
  );
  return (
    /[{}<>:"|?*\u0000-\u001f\u007f-\u009f]/.test(remaining) ||
    remaining
      .split("/")
      .some((segment) => /^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)/i.test(segment))
  );
}

export function deliverySettingsAreInvalid(value: DeliverySettings) {
  return (
    deliveryTemplateIsInvalid(value.directoryTemplate) ||
    deliveryTemplateIsInvalid(value.partDirectoryTemplate, true) ||
    !deliveryContentLayoutIsValid(value.contentLayout)
  );
}

/** Shared global defaults and pre-execution task overrides; the server freezes the actual paths. */
export default function DeliverySettingsFields({
  value,
  onChange,
  disabled,
  downloadRoot,
  taskId = 123,
  title,
  showHint = true,
  rootAside,
}: {
  value: DeliverySettings;
  onChange: (value: DeliverySettings) => void;
  disabled?: boolean;
  downloadRoot?: string | null;
  taskId?: number;
  title?: string | null;
  showHint?: boolean;
  rootAside?: ReactNode;
}) {
  const { t } = useTranslation();
  const safeTitle = String(title || t("acquisition.setup.template.exampleTitle") || "Resource")
    .replace(/[\\/<>:"|?*\u0000-\u001f]/g, "_")
    .slice(0, 100);
  const taskPath = value.directoryTemplate
    .replace(/\{Title\}/g, () => safeTitle)
    .replace(/\{TaskId\}/g, () => String(taskId));
  const root =
    value.rootDirectory?.trim() ||
    `${downloadRoot?.replace(/[\\/]+$/, "") || t("acquisition.delivery.downloadRoot")}/Acquired`;
  return (
    <section className="space-y-3">
      {showHint && <p className="text-xs text-default-500">{t("acquisition.delivery.hint")}</p>}
      <div className="space-y-1">
        <h3 className="text-sm font-medium">{t("acquisition.delivery.root")}</h3>
        <div className="flex flex-wrap items-center gap-2">
          <FileSystemSelectorButton
            key={value.rootDirectory || "inherited"}
            aria-label={t("acquisition.delivery.chooseRoot")}
            className="h-auto min-h-8 max-w-full whitespace-normal break-all text-left"
            isDisabled={disabled}
            fileSystemSelectorProps={{
              targetType: "folder",
              multiple: false,
              startPath: value.rootDirectory || downloadRoot || undefined,
              defaultSelectedPath: value.rootDirectory || undefined,
              onSelected: (entry) => {
                if (entry.path?.trim()) onChange({ ...value, rootDirectory: entry.path });
              },
            }}
          >
            {t("acquisition.delivery.chooseRoot")}
          </FileSystemSelectorButton>
          {!!value.rootDirectory && (
            <Button
              size="sm"
              variant="light"
              isDisabled={disabled}
              onPress={() => onChange({ ...value, rootDirectory: "" })}
            >
              {t("acquisition.delivery.inheritRoot")}
            </Button>
          )}
        </div>
        {rootAside}
        <p className="text-xs text-default-500">{t("acquisition.delivery.rootHint")}</p>
      </div>
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <Input
          label={t("acquisition.delivery.taskTemplate")}
          description={t("acquisition.delivery.taskTemplateHint")}
          value={value.directoryTemplate}
          isDisabled={disabled}
          isInvalid={deliveryTemplateIsInvalid(value.directoryTemplate)}
          errorMessage={t("acquisition.delivery.invalidTemplate")}
          onValueChange={(directoryTemplate) => onChange({ ...value, directoryTemplate })}
        />
        <Input
          label={t("acquisition.delivery.partTemplate")}
          description={t("acquisition.delivery.partTemplateHint")}
          value={value.partDirectoryTemplate}
          isDisabled={disabled}
          isInvalid={deliveryTemplateIsInvalid(value.partDirectoryTemplate, true)}
          errorMessage={t("acquisition.delivery.invalidPartTemplate")}
          onValueChange={(partDirectoryTemplate) => onChange({ ...value, partDirectoryTemplate })}
        />
      </div>
      <div className="rounded-lg bg-default-100 p-2 text-xs" aria-live="polite">
        <span className="text-default-500">{t("acquisition.delivery.preview")}: </span>
        <code className="break-all">
          {root.replace(/[\\/]+$/, "")}/{taskPath}
        </code>
      </div>
      {value.contentLayout && !deliveryContentLayoutsEqual(value.contentLayout, defaultDeliveryContentLayout()) ? (
        <div className="flex flex-wrap items-center gap-2">
          <p className="text-xs text-default-500">{t("artifact.deliveryLayout.customPolicy")}</p>
          <Button size="sm" variant="flat" isDisabled={disabled}
            onPress={() => onChange({ ...value, contentLayout: defaultDeliveryContentLayout() })}>
            {t("artifact.deliveryLayout.restoreDefault")}
          </Button>
        </div>
      ) : (
        <p className="text-xs text-default-500">{t("artifact.deliveryLayout.acquisitionDefault")}</p>
      )}
    </section>
  );
}
