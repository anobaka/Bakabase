import type { ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, Input, Select } from "@/components/bakaui";
import FileSystemSelectorButton from "@/components/FileSystemSelector/components/Button";
import type {
  BakabaseModulesWorkflowArtifactsArtifactStoragePolicy as StoragePolicy,
  BakabaseModulesWorkflowArtifactsArtifactStoragePolicyOverride as StoragePolicyOverride,
} from "@/sdk/Api";

export type { StoragePolicy, StoragePolicyOverride };
export const defaultStoragePolicy: StoragePolicy = {
  version: 1,
  downloadDirectoryTemplate: "{Title} [D{OperationId}]",
  processingDirectoryTemplate: "{Title}/operation-{OperationId}/attempt-{Attempt}",
  processingWorkspaceDirectory: "",
  cleanupIntermediates: true,
  materialization: "auto",
  sourceCleanup: "keep",
};
export const storagePoliciesEqual = (left: StoragePolicy, right: StoragePolicy) =>
  (Object.keys(defaultStoragePolicy) as (keyof StoragePolicy)[]).every(
    (key) => (left[key] ?? defaultStoragePolicy[key]) === (right[key] ?? defaultStoragePolicy[key]),
  );

export const resolveStoragePolicy = (
  parent: StoragePolicy,
  override?: StoragePolicyOverride | null,
): StoragePolicy => ({
  ...parent,
  ...Object.fromEntries(Object.entries(override ?? {}).filter(([, value]) => value != null)),
});

export function storageTemplateIsInvalid(template: string, processing = false): boolean {
  const allowed = processing
    ? ["Title", "OperationId", "Kind", "Attempt"]
    : ["Title", "OperationId", "Provider"];
  const required = processing ? ["OperationId", "Attempt"] : ["OperationId"];
  if (
    !template.trim() ||
    template.length > 240 ||
    /[\\\u0000-\u001f\u007f-\u009f]/.test(template) ||
    template.split("/").length > 5 ||
    required.some((token) => !template.includes(`{${token}}`))
  )
    return true;
  const worstCase = template.replace(/\{([^{}]+)\}/g, (_, token) =>
    "x".repeat(token === "OperationId" ? 64 : token === "Attempt" ? 10 : 81),
  );
  if (worstCase.split("/").some((part) => new TextEncoder().encode(part).length > 240)) return true;
  let unknown = false;
  const rest = template.replace(/\{([^{}]+)\}/g, (_, token) => {
    if (!allowed.includes(token)) unknown = true;
    return "value";
  });
  return (
    unknown ||
    /[{}]/.test(rest) ||
    rest
      .split("/")
      .some(
        (part) =>
          !part ||
          part === "." ||
          part === ".." ||
          /[. ]$/.test(part) ||
          /[<>:"|?*]/.test(part) ||
          /^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)/i.test(part),
      )
  );
}
export const storagePolicyIsInvalid = (value: StoragePolicy) =>
  value.version !== 1 ||
  storageTemplateIsInvalid(value.downloadDirectoryTemplate) ||
  storageTemplateIsInvalid(value.processingDirectoryTemplate, true) ||
  !["keep", "quarantine", "delete"].includes(value.sourceCleanup) ||
  !["auto", "copy"].includes(value.materialization ?? "auto");

/** Shared caller-preset, task and node editor. Paths are chosen through the filesystem selector. */
export default function StoragePolicyFields({
  value,
  onChange,
  disabled,
  processingOnly = false,
  showWorkspace = !processingOnly,
  showHint = true,
  workspaceAside,
}: {
  value: StoragePolicy;
  onChange: (value: StoragePolicy) => void;
  disabled?: boolean;
  processingOnly?: boolean;
  showWorkspace?: boolean;
  showHint?: boolean;
  workspaceAside?: ReactNode;
}) {
  const { t } = useTranslation();
  const update = (change: Partial<StoragePolicy>) => onChange({ ...value, ...change });
  return (
    <div className="space-y-3">
      {showHint && (
        <p className="text-xs text-default-500">{t("acquisition.storage.inheritanceHint")}</p>
      )}
      {!processingOnly && (
        <Input
          size="sm"
          label={t("acquisition.storage.downloadTemplate")}
          description={t("acquisition.storage.downloadTemplateHint")}
          value={value.downloadDirectoryTemplate}
          isDisabled={disabled}
          isInvalid={storageTemplateIsInvalid(value.downloadDirectoryTemplate)}
          errorMessage={t("acquisition.storage.templateInvalid")}
          onValueChange={(downloadDirectoryTemplate) => update({ downloadDirectoryTemplate })}
        />
      )}
      <Input
        size="sm"
        label={t("acquisition.storage.processingTemplate")}
        description={t("acquisition.storage.processingTemplateHint")}
        value={value.processingDirectoryTemplate}
        isDisabled={disabled}
        isInvalid={storageTemplateIsInvalid(value.processingDirectoryTemplate, true)}
        errorMessage={t("acquisition.storage.templateInvalid")}
        onValueChange={(processingDirectoryTemplate) => update({ processingDirectoryTemplate })}
      />
      {showWorkspace && (
        <div className="space-y-2">
          <div className="text-sm font-medium">{t("acquisition.workspace.title")}</div>
          <div className="flex flex-wrap items-center gap-2">
            <FileSystemSelectorButton
              key={value.processingWorkspaceDirectory || "alongside-input"}
              className="h-auto min-h-8 max-w-full whitespace-normal break-all text-left"
              aria-label={t("acquisition.workspace.chooseDirectory")}
              isDisabled={disabled}
              fileSystemSelectorProps={{
                targetType: "folder",
                multiple: false,
                startPath: value.processingWorkspaceDirectory || undefined,
                defaultSelectedPath: value.processingWorkspaceDirectory || undefined,
                onSelected: (entry) => {
                  if (entry.path?.trim()) update({ processingWorkspaceDirectory: entry.path });
                },
              }}
            >
              {t("acquisition.workspace.chooseDirectory")}
            </FileSystemSelectorButton>
            {value.processingWorkspaceDirectory && (
              <Button
                size="sm"
                variant="light"
                isDisabled={disabled}
                onPress={() => update({ processingWorkspaceDirectory: "" })}
              >
                {t("acquisition.workspace.useInputStorage")}
              </Button>
            )}
          </div>
          {workspaceAside}
          <p className="text-xs text-default-500">{t("acquisition.workspace.description")}</p>
        </div>
      )}
      <Select
        size="sm"
        label={t("workflow.storage.materialization.label")}
        description={t("workflow.storage.materialization.description")}
        selectedKeys={new Set([value.materialization ?? "auto"])}
        isDisabled={disabled}
        disallowEmptySelection
        dataSource={["auto", "copy"].map((mode) => ({
          value: mode,
          label: t(`workflow.storage.materialization.${mode}`),
        }))}
        onSelectionChange={(keys) => {
          const mode = String([...keys][0]);
          if (["auto", "copy"].includes(mode)) update({ materialization: mode });
        }}
      />
      <Checkbox
        size="sm"
        isSelected={value.cleanupIntermediates}
        isDisabled={disabled}
        onValueChange={(cleanupIntermediates) => update({ cleanupIntermediates })}
      >
        {t("acquisition.storage.cleanupIntermediates")}
      </Checkbox>
      <p className="text-xs text-default-500">
        {t("acquisition.storage.cleanupIntermediatesHint")}
      </p>
      {!processingOnly && (
        <Select
          size="sm"
          label={t("acquisition.storage.sourceCleanup")}
          selectedKeys={new Set([value.sourceCleanup])}
          isDisabled={disabled}
          disallowEmptySelection
          onSelectionChange={(keys) => {
            const choice = [...keys][0];
            if (["keep", "quarantine", "delete"].includes(String(choice)))
              update({ sourceCleanup: String(choice) });
          }}
          description={t("acquisition.storage.sourceCleanupHint")}
          dataSource={["keep", "quarantine", "delete"].map((mode) => ({
            value: mode,
            label: t(`acquisition.storage.sourceCleanup.${mode}`),
            textValue: t(`acquisition.storage.sourceCleanup.${mode}`),
            description: t(`acquisition.storage.sourceCleanup.${mode}Hint`),
          }))}
        />
      )}
    </div>
  );
}
