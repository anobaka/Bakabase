import ArchivePasswordInput from "@/components/PasswordSelector/ArchivePasswordInput";
import {
  EditOutlined,
  FileZipOutlined,
  FolderOpenOutlined,
  FolderOutlined,
  FontSizeOutlined,
  FormOutlined,
  SlidersOutlined,
} from "@ant-design/icons";
import { lazy, Suspense, useId, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import { Button, Input, Select, Textarea } from "@/components/bakaui";
import ProcessingChoiceGroup from "./ProcessingChoiceGroup";
import ExtractionLayoutField from "./ExtractionLayoutField";
import ZipFileNameCodePageField from "./ZipFileNameCodePageField";
import ZipPasswordCodePageField from "./ZipPasswordCodePageField";
import { isZipPasswordCodePage } from "./zipPasswordCodePage";
import DependencyManager from "@/components/Dependency/DependencyManager";
import ArchiveOpenModeField from "./ArchiveOpenModeField";
import { isCompleteArchiveOpening } from "./archiveOpenMode";
import {
  appendProcessingStep,
  canRemoveProcessingStep,
  isCompleteDirectoryRebase,
  processingOperations,
  readEditableProcessingPlan,
  removeProcessingStep,
  stepInputs,
  updateProcessingStep,
  type EditableProcessingPlan,
  type ProcessingOperation,
} from "./processingPlanEditor";

const ProcessingSourceDirectoryPicker = lazy(() => import("./ProcessingSourceDirectoryPicker"));

const operationIcons = {
  extractArchive: FileZipOutlined,
  renameExtension: FormOutlined,
  renameFile: EditOutlined,
  renameLiteral: FontSizeOutlined,
  moveFile: FolderOutlined,
  rebaseDirectory: FolderOpenOutlined,
};

function StepOptions({ configured, children }: { configured: boolean; children: ReactNode }) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(configured);
  const id = useId();
  return (
    <div>
      <Button
        type="button"
        size="sm"
        variant="light"
        aria-expanded={open}
        aria-controls={id}
        startContent={<SlidersOutlined aria-hidden />}
        onPress={() => setOpen(!open)}
      >
        {t("workflow.processing.editor.stepOptions")}
      </Button>
      {open && (
        <div id={id} className="mt-1 grid gap-2 sm:grid-cols-2">
          {children}
        </div>
      )}
    </div>
  );
}

function hasRequiredFields(step: Record<string, unknown>): boolean {
  switch (step.op) {
    case "renameExtension":
      return (
        typeof step.extension === "string" &&
        /^\.[a-zA-Z0-9]{1,16}(?:\.[a-zA-Z0-9]{1,16}){0,2}$/.test(step.extension)
      );
    case "renameFile":
      return typeof step.targetName === "string" && !!step.targetName.trim();
    case "renameLiteral":
      return (
        typeof step.findText === "string" &&
        step.findText.length > 0 &&
        typeof step.replaceText === "string"
      );
    case "moveFile":
      return typeof step.targetDirectory === "string" && !!step.targetDirectory.trim();
    case "rebaseDirectory":
      return isCompleteDirectoryRebase(step);
    default:
      return true;
  }
}

/** Basic editor completeness only; the server owns the full executable-plan validation. */
export function isCompleteProcessingPlan(json: string): boolean {
  try {
    const value: unknown = JSON.parse(json);
    if (
      !value ||
      typeof value !== "object" ||
      Array.isArray(value) ||
      !("steps" in value) ||
      !Array.isArray(value.steps) ||
      !("requirement" in value)
    )
      return false;
    return value.requirement === "notRequired"
      ? value.steps.length === 0
      : value.requirement === "required" &&
          value.steps.length > 0 &&
          value.steps.every(
            (step) =>
              !!step &&
              typeof step === "object" &&
              hasRequiredFields(step) &&
              isCompleteArchiveOpening(step) &&
              (step.zipPasswordCodePage == null ||
                (step.op === "extractArchive" && isZipPasswordCodePage(step.zipPasswordCodePage))),
          );
  } catch {
    return false;
  }
}

export default function ProcessingPlanField({
  value,
  onChange,
  inputRootDirectory,
  isDisabled = false,
  stepsOnly = false,
}: {
  value: string;
  onChange: (value: string) => void;
  inputRootDirectory?: string | null;
  isDisabled?: boolean;
  stepsOnly?: boolean;
}) {
  const { t } = useTranslation();
  const [help, setHelp] = useState(false);
  const [container, setContainer] = useState<HTMLElement | null>(null);
  const popoverProps = {
    portalContainer: container?.closest<HTMLElement>('[role="dialog"]') ?? undefined,
  };
  const [advanced, setAdvanced] = useState(false);
  const plan = readEditableProcessingPlan(value);
  const showJson = advanced || !plan;
  const write = (next: EditableProcessingPlan) => onChange(JSON.stringify(next, null, 2));
  const stepName = (index: number, op: string) =>
    `${t("workflow.processing.editor.step", { index: index + 1 })} · ${t(`workflow.processing.editor.operation.${op}`)}`;
  return (
    <div ref={setContainer} className="min-w-0 space-y-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="text-sm font-medium">{t("workflow.processing.editor.title")}</p>
        <Button
          size="sm"
          variant="light"
          isDisabled={!plan && showJson}
          onPress={() => setAdvanced(!showJson)}
        >
          {t(
            showJson ? "workflow.processing.editor.visual" : "workflow.processing.editor.advanced",
          )}
        </Button>
      </div>
      {!plan && (
        <p className="text-xs text-warning-600">
          {t("workflow.processing.editor.advancedRequired")}
        </p>
      )}
      {showJson ? (
        <Textarea
          label={t("workflow.processing.plan")}
          value={value}
          onValueChange={onChange}
          minRows={8}
          isDisabled={isDisabled}
          description={t("workflow.processing.planEditorHint")}
        />
      ) : (
        plan && (
          <>
            {!stepsOnly && (
              <ProcessingChoiceGroup<EditableProcessingPlan["requirement"]>
                label={t("workflow.processing.editor.requirement")}
                value={plan.requirement}
                isDisabled={isDisabled}
                choices={[
                  {
                    value: "required",
                    label: t("workflow.processing.editor.requirement.required"),
                    icon: "manual",
                  },
                  {
                    value: "notRequired",
                    label: t("workflow.processing.editor.requirement.notRequired"),
                    icon: "keep",
                    isDisabled: plan.steps.length > 0,
                  },
                  {
                    value: "unknown",
                    label: t("workflow.processing.editor.requirement.unknown"),
                    icon: "defer",
                  },
                ]}
                onChange={(requirement) => write({ ...plan, requirement })}
              />
            )}
            {plan.steps.map((step, index) => {
              const inputs = stepInputs(step);
              const multipleInputs = inputs.length > 1;
              const patch = (changes: Record<string, unknown>) =>
                write(updateProcessingStep(plan, step.id, changes));
              const input = (
                key: string,
                options?: { description?: string; required?: boolean },
              ) => (
                <Input
                  key={key}
                  size="sm"
                  label={t(`workflow.processing.editor.field.${key}`)}
                  value={typeof step[key] === "string" ? (step[key] as string) : ""}
                  isDisabled={isDisabled}
                  isRequired={options?.required}
                  type="text"
                  autoComplete="off"
                  description={options?.description}
                  onValueChange={(next) =>
                    patch({
                      [key]: key === "archiveMemberSelector" && next === "" ? undefined : next,
                    })
                  }
                />
              );
              const Icon = operationIcons[step.op];
              const configuredOptions =
                multipleInputs ||
                inputs[0] !== (index ? plan.steps[index - 1].id : "download") ||
                step.extractionLayout != null ||
                step.archiveOpenMode != null ||
                step.zipFileNameCodePage != null ||
                step.zipPasswordCodePage != null;
              const removable = canRemoveProcessingStep(plan, step.id);
              return (
                <section
                  key={step.id}
                  aria-label={stepName(index, step.op)}
                  className="space-y-2 border-l-2 border-default-200 pl-3"
                >
                  <div className="flex items-center justify-between gap-2">
                    <h4 className="flex items-center gap-2 text-sm font-medium">
                      <Icon aria-hidden />
                      {stepName(index, step.op)}
                    </h4>
                    <Button
                      size="sm"
                      variant="light"
                      color="danger"
                      isDisabled={isDisabled || !removable}
                      onPress={() => write(removeProcessingStep(plan, step.id))}
                    >
                      {t("workflow.processing.editor.remove")}
                    </Button>
                  </div>
                  {!removable && (
                    <p className="text-xs text-default-500">
                      {t("workflow.processing.editor.referenced")}
                    </p>
                  )}
                  <div className="grid items-start gap-2 sm:grid-cols-2">
                    {step.op !== "rebaseDirectory" &&
                      input("selector", {
                        description: t("workflow.processing.editor.selectorHint"),
                        required: step.archiveOpenMode != null,
                      })}
                    {step.op === "rebaseDirectory" && (
                      <>
                        <div className="min-w-0 space-y-1">
                          {input("sourceDirectory", {
                            required: true,
                            description: t("workflow.processing.editor.sourceDirectoryHint"),
                          })}
                          {inputRootDirectory &&
                            inputs.length === 1 &&
                            inputs[0] === "download" && (
                              <Suspense fallback={null}>
                                <ProcessingSourceDirectoryPicker
                                  key={JSON.stringify([inputRootDirectory, step.sourceDirectory])}
                                  root={inputRootDirectory}
                                  disabled={isDisabled}
                                  onSelect={(sourceDirectory) => patch({ sourceDirectory })}
                                />
                              </Suspense>
                            )}
                        </div>
                        {input("targetDirectory", {
                          description: t("workflow.processing.editor.rebaseDirectoryHint"),
                        })}
                      </>
                    )}
                    {step.op === "extractArchive" && (
                      <ArchivePasswordInput
                        size="sm"
                        label={t("workflow.processing.editor.field.password")}
                        value={typeof step.password === "string" ? step.password : ""}
                        isDisabled={isDisabled}
                        onValueChange={(password) => patch({ password })}
                      />
                    )}
                    {step.op === "renameExtension" &&
                      input("extension", {
                        required: true,
                        description: t("workflow.processing.editor.extensionHint"),
                      })}
                    {step.op === "renameFile" && input("targetName", { required: true })}
                    {step.op === "moveFile" &&
                      input("targetDirectory", {
                        required: true,
                        description: t("workflow.processing.editor.directoryHint"),
                      })}
                    {step.op === "renameLiteral" && (
                      <>
                        {input("findText", { required: true })}
                        {input("replaceText")}
                      </>
                    )}
                  </div>
                  <StepOptions configured={configuredOptions}>
                    <Select
                      popoverProps={popoverProps}
                      size="sm"
                      label={t("workflow.processing.editor.operation")}
                      selectedKeys={[step.op]}
                      isDisabled={isDisabled}
                      dataSource={processingOperations.map((op) => ({
                        value: op,
                        label: t(`workflow.processing.editor.operation.${op}`),
                      }))}
                      onSelectionChange={(keys) => {
                        const op = String(Array.from(keys)[0] ?? "") as ProcessingOperation;
                        if (processingOperations.includes(op)) patch({ op });
                      }}
                    />
                    {multipleInputs ? (
                      <div className="rounded-lg bg-default-100 p-2 text-xs text-default-600">
                        <p>{t("workflow.processing.editor.multipleInputs")}</p>
                        <p className="break-all">{inputs.join(", ")}</p>
                      </div>
                    ) : (
                      <Select
                        popoverProps={popoverProps}
                        size="sm"
                        label={t("workflow.processing.editor.input")}
                        selectedKeys={inputs}
                        isDisabled={isDisabled}
                        dataSource={[
                          {
                            value: "download",
                            label: t("workflow.processing.editor.initialInput"),
                          },
                          ...plan.steps.slice(0, index).map((previous, previousIndex) => ({
                            value: previous.id,
                            label: stepName(previousIndex, previous.op),
                          })),
                        ]}
                        onSelectionChange={(keys) => {
                          const next = String(Array.from(keys)[0] ?? "");
                          if (
                            next === "download" ||
                            plan.steps.slice(0, index).some((previous) => previous.id === next)
                          )
                            patch(step.inputs != null ? { inputs: [next] } : { input: next });
                        }}
                      />
                    )}
                    {step.op === "extractArchive" && (
                      <ExtractionLayoutField
                        step
                        value={step.extractionLayout}
                        onChange={(extractionLayout) => patch({ extractionLayout })}
                        isDisabled={isDisabled}
                      />
                    )}
                    {step.op === "extractArchive" && (
                      <ArchiveOpenModeField
                        value={step.archiveOpenMode}
                        onChange={(archiveOpenMode) => patch({ archiveOpenMode })}
                        isDisabled={isDisabled}
                      />
                    )}
                    {step.op === "extractArchive" &&
                      step.archiveOpenMode != null &&
                      input("archiveMemberSelector", {
                        description: t("workflow.processing.archiveOpenMode.memberHint"),
                      })}
                    {step.op === "extractArchive" && step.archiveOpenMode == null && (
                      <ZipPasswordCodePageField
                        value={step.zipPasswordCodePage}
                        onChange={(zipPasswordCodePage) => patch({ zipPasswordCodePage })}
                        isDisabled={isDisabled}
                      />
                    )}
                    {step.op === "extractArchive" && step.archiveOpenMode == null && (
                      <ZipFileNameCodePageField
                        value={step.zipFileNameCodePage}
                        onChange={(zipFileNameCodePage) => patch({ zipFileNameCodePage })}
                        isDisabled={isDisabled}
                      />
                    )}
                  </StepOptions>
                </section>
              );
            })}
            {plan.steps.some((step) => step.zipPasswordCodePage != null) && (
              <div className="rounded-lg border border-default-200 p-3">
                <p className="mb-2 text-sm text-default-500">
                  {t("workflow.processing.zipPasswordCodePage.dependency")}
                </p>
                <DependencyManager id="pyzipper-component-service" mode="summary" />
              </div>
            )}
            {plan.requirement !== "notRequired" && (
              <div
                role="group"
                aria-label={t("workflow.processing.editor.addOperation")}
                className="space-y-1"
              >
                <p className="text-sm font-medium">
                  {t("workflow.processing.editor.addOperation")}
                </p>
                <div className="flex flex-wrap gap-1.5">
                  {processingOperations.map((op) => {
                    const Icon = operationIcons[op];
                    return (
                      <Button
                        key={op}
                        type="button"
                        size="sm"
                        variant="flat"
                        startContent={<Icon aria-hidden />}
                        isDisabled={isDisabled || plan.steps.length >= 24}
                        onPress={() => write(appendProcessingStep(plan, op))}
                      >
                        {t(`workflow.processing.editor.operation.${op}`)}
                      </Button>
                    );
                  })}
                </div>
              </div>
            )}
          </>
        )
      )}
      {showJson && (
        <Button size="sm" variant="light" onPress={() => setHelp((current) => !current)}>
          {t("workflow.processing.planSyntax")}
        </Button>
      )}
      {showJson && help && (
        <dl className="space-y-2 rounded-lg bg-default-50 p-3 text-xs text-default-500">
          {(
            [
              "requirement",
              "references",
              "inputs",
              "selector",
              "renameExtension",
              "renameFile",
              "renameLiteral",
              "moveFile",
              "rebaseDirectory",
              "extractArchive",
              "executableArchive",
            ] as const
          ).map((field) => (
            <div key={field}>
              <dt className="font-mono text-default-700">
                {field === "executableArchive" ? "extractArchive (.exe)" : field}
              </dt>
              <dd className="break-words">{t(`workflow.processing.planSyntax.${field}`)}</dd>
            </div>
          ))}
        </dl>
      )}
    </div>
  );
}
