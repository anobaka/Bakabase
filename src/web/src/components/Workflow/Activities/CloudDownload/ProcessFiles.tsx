import ArchivePasswordInput from "@/components/PasswordSelector/ArchivePasswordInput";
import type { WorkflowActivityUI } from "../types";
import { downloadWorksetExecutionLabels, fileProcessingExecutionLabels } from "../executionLabels";
import { useContext, useEffect, useState, type FC } from "react";
import { useTranslation } from "react-i18next";
import {
  Accordion,
  AccordionItem,
  Button,
  NumberInput,
  Select,
  Switch,
  Textarea,
} from "@/components/bakaui";
import TaskFilePlanDecision from "./TaskFilePlanDecision";
import ResumeActions from "../../ResumeActions";
import { WorkflowActivityCategory } from "@/sdk/constants";
import ReusePolicyField, { isReusePolicy, type ReusePolicy } from "./ReusePolicy";
import { ResumeGuidance, ResumePostSourceContext, useResumeActivityKind } from "../../ResumePresentation";
import ProcessingPlanField, { isCompleteProcessingPlan } from "../../ProcessingPlanField";
import ExtractionLayoutField from "../../ExtractionLayoutField";
import { isExtractionLayout, type ExtractionLayout } from "../../extractionLayout";
import { localStorageProblemCode } from "@/components/CloudDownload/problemLabels";
import StoragePolicyFields, {
  defaultStoragePolicy,
  storagePolicyIsInvalid,
  type StoragePolicy,
} from "../../ArtifactStorage/PolicyFields";
import FileSystemSelectorButton from "@/components/FileSystemSelector/components/Button";
import { fileProcessingRecoveryReasons, canRetryFileProcessing, fileProcessingFailureReason } from "../../fileProcessingFailure";
import DependencyManager from "@/components/Dependency/DependencyManager";
import ProcessingPathConflictSummary from "../../ProcessingPathConflictSummary";
import { assessmentDiagnosticFromWait, assessmentExplanationFromWait } from "../../adaptiveAssessmentDiagnostic";
import ProcessingProblemSummary from "../../ProcessingProblemSummary";

export interface ProcessingConfig {
  extractionLayout?: ExtractionLayout;
  storagePolicy?: StoragePolicy | null;
  workspaceRootDirectory?: string | null;
  reusePolicy: ReusePolicy;
  archivePolicy: "instructions" | "autoDetect" | "keep";
  recursive: {
    enabled: boolean;
    maxDepth: number;
    maxArchiveGroups: number;
    maxFiles: number;
    maxExpandedBytes: number;
    minimumFreeBytes: number;
  };
}

const recursiveDefaults: ProcessingConfig["recursive"] = {
  enabled: false,
  maxDepth: 8,
  maxArchiveGroups: 1_000,
  maxFiles: 10_000,
  maxExpandedBytes: 21_474_836_480,
  minimumFreeBytes: 1_073_741_824,
};

function inheritedProcessingConfig(): ProcessingConfig {
  return {
    reusePolicy: "reuseVerified",
    archivePolicy: "instructions",
    recursive: { ...recursiveDefaults },
  };
}

export function defaultProcessingConfig(): ProcessingConfig {
  return { ...inheritedProcessingConfig(), extractionLayout: "currentDirectory" };
}

export function parseProcessingConfig(json: string): ProcessingConfig {
  try {
    const value: unknown = JSON.parse(json || "{}");
    if (!value || typeof value !== "object" || Array.isArray(value))
      return inheritedProcessingConfig();
    const config = value as Partial<ProcessingConfig>;
    return {
      ...(config.extractionLayout != null ? { extractionLayout: config.extractionLayout } : {}),
      reusePolicy: config.reusePolicy ?? "reuseVerified",
      archivePolicy: config.archivePolicy ?? "instructions",
      ...(config.storagePolicy != null
        ? { storagePolicy: { ...defaultStoragePolicy, ...config.storagePolicy } }
        : {}),
      ...(config.workspaceRootDirectory != null
        ? { workspaceRootDirectory: config.workspaceRootDirectory }
        : {}),
      recursive: { ...recursiveDefaults, ...config.recursive },
    };
  } catch {
    return inheritedProcessingConfig();
  }
}

function isValidConfig(config: ProcessingConfig): boolean {
  return (
    isReusePolicy(config.reusePolicy) &&
    (config.extractionLayout == null || isExtractionLayout(config.extractionLayout)) &&
    (!config.storagePolicy || !storagePolicyIsInvalid(config.storagePolicy)) &&
    ["instructions", "autoDetect", "keep"].includes(config.archivePolicy) &&
    typeof config.recursive?.enabled === "boolean" &&
    [
      config.recursive.maxDepth,
      config.recursive.maxArchiveGroups,
      config.recursive.maxFiles,
      config.recursive.maxExpandedBytes,
    ].every((value) => Number.isSafeInteger(value) && value > 0) &&
    Number.isSafeInteger(config.recursive.minimumFreeBytes) &&
    config.recursive.minimumFreeBytes >= 0
  );
}

function limitsPayload(options: ProcessingConfig["recursive"]) {
  return {
    maxDepth: options.maxDepth,
    maxArchiveGroups: options.maxArchiveGroups,
    maxFiles: options.maxFiles,
    maxExpandedBytes: options.maxExpandedBytes,
    minimumFreeBytes: options.minimumFreeBytes,
  };
}

interface ProcessingPrompt {
  targetId?: string;
  title?: string;
  reason?: string;
  message?: string;
  extractionPlanJson?: string;
  archiveName?: string;
  tried?: string[];
  depth?: number;
  archiveId?: string;
  sourcePasswordEvidenceChecked?: boolean;
  recursiveOptions?: Omit<ProcessingConfig["recursive"], "enabled">;
  allowedArchivePolicies?: ProcessingConfig["archivePolicy"][];
  canEditPlan?: boolean;
  knownStepId?: string;
  requiredComponentId?: string | null;
  allowedActions?: string[];
  deliveryReview?: boolean;
  files?: string[];
  pathConflict?: unknown;
  adaptiveAssessment?: boolean;
}

function parsePrompt(json: string | null): ProcessingPrompt {
  try {
    const value: unknown = JSON.parse(json ?? "{}");
    return value && typeof value === "object" && !Array.isArray(value)
      ? (value as ProcessingPrompt)
      : {};
  } catch {
    return {};
  }
}

const ResumeForm: NonNullable<WorkflowActivityUI<ProcessingConfig>["ResumeForm"]> = ({
  promptJson,
  submitting,
  onSubmit,
  fileTask = false,
}) => {
  const { t } = useTranslation();
  const prompt = parsePrompt(promptJson);
  const hasPostSource = useContext(ResumePostSourceContext);
  const activityKind = useResumeActivityKind();
  const [password, setPassword] = useState("");
  const [userContext, setUserContext] = useState("");
  const [archivePolicy, setArchivePolicy] =
    useState<ProcessingConfig["archivePolicy"]>("instructions");
  const [plan, setPlan] = useState(prompt.extractionPlanJson ?? "");
  const [recursiveOptions, setRecursiveOptions] = useState({
    ...recursiveDefaults,
    ...prompt.recursiveOptions,
  });
  useEffect(() => {
    setPassword("");
    setUserContext("");
    setArchivePolicy("instructions");
    setPlan(parsePrompt(promptJson).extractionPlanJson ?? "");
    setRecursiveOptions({ ...recursiveDefaults, ...parsePrompt(promptJson).recursiveOptions });
  }, [promptJson]);
  const reason = fileProcessingFailureReason(prompt.reason, prompt.message);
  const assessmentDiagnostic = prompt.adaptiveAssessment && assessmentDiagnosticFromWait(prompt.message);
  const assessmentExplanation = assessmentExplanationFromWait(prompt);
  const recoveryGuidance = fileProcessingRecoveryReasons.has(reason ?? "");
  const retryCurrentStep = canRetryFileProcessing(reason) &&
    (prompt.allowedActions == null || prompt.allowedActions.includes("retryCurrentStep") ||
      // Older runs saved no actions before bounded ZIP-window support was available.
      (reason === "ArchiveProbePrefixLimit" && prompt.allowedActions.length === 0));
  const needsPassword = reason === "PasswordUnknown";
  const canCheckPostPassword = needsPassword && hasPostSource && !!prompt.targetId &&
    prompt.sourcePasswordEvidenceChecked !== true &&
    (activityKind === "acquisition.plan.process" || activityKind === "fs.assessDelivery");
  const pathConflict = reason === "ProcessingPathConflict";
  const needsPlan =
    ["ExtractionPlanUnknown", "ExtractionLayoutConflict", "ProcessingPathConflict", "ArchiveInputRequired", "ProcessingInputNotMatched", "ArchiveNameEncodingUnsupported", "ArchivePasswordEncodingUnsupported", "ArchivePasswordNotRepresentable", "ArchiveGroupInputChanged", "FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED", "FILE_DELIVERY_RESULT_UNCERTAIN", ...(prompt.deliveryReview ? ["RepeatedContent"] : [])].includes(reason ?? "") &&
    prompt.canEditPlan !== false;
  const needsBudget = [
    "RecursiveBudgetExceeded",
    "DepthLimit",
    "ArchiveCountLimit",
    "FileCountLimit",
    "ExpandedBytesLimit",
    "DiskSpaceLimit",
  ].includes(prompt.reason ?? "");
  const repeatedContent = prompt.reason === "RepeatedContent";
  const waitingShared = prompt.reason === "WaitingShared";
  const localStorageProblem =
    localStorageProblemCode(prompt.reason) ?? localStorageProblemCode(prompt.message);
  const storageUnavailable = prompt.reason === "StorageUnavailable" || !!localStorageProblem;
  const validPlan = isCompleteProcessingPlan(plan) &&
    (!pathConflict || plan !== prompt.extractionPlanJson) && (!prompt.deliveryReview || (() => {
    try {
      const value = JSON.parse(plan);
      return value?.requirement === "required" && Array.isArray(value.steps) && value.steps.length > 0;
    } catch { return false; }
  })());
  const valid =
    !!prompt.targetId &&
    (needsPassword
      ? password.length > 0
      : needsPlan
        ? (userContext.trim().length > 0 && prompt.adaptiveAssessment
          ? true
          : (!prompt.allowedArchivePolicies ||
            prompt.allowedArchivePolicies.includes(archivePolicy)) &&
            (archivePolicy !== "instructions" || validPlan))
        : retryCurrentStep || waitingShared ||
          storageUnavailable ||
          (needsBudget &&
            isValidConfig({
              reusePolicy: "reuseVerified",
              archivePolicy: "instructions",
              recursive: recursiveOptions,
            })));
  const guide = needsPassword
    ? "password"
    : needsPlan
      ? "plan"
      : needsBudget
        ? "budget"
        : repeatedContent
          ? "repeated"
          : localStorageProblem
            ? localStorageProblem === "LOCAL_STORAGE_RESERVE_LOW" ? "storageReserve" : "localStorageProcessing"
            : storageUnavailable
              ? "storage"
              : waitingShared
                ? "shared"
                : "processingUnknown";
  const guidancePrefix = ["FILE_DELIVERY_ARCHIVE_REVIEW_REQUIRED", "FILE_DELIVERY_RESULT_UNCERTAIN"].includes(reason ?? "")
    ? "cloudDownload.resumeGuide.archiveDelivery"
    : recoveryGuidance || reason === "ExtractionLayoutConflict" ? `workflow.fileRecovery.${reason}` : `cloudDownload.resumeGuide.${guide}`;
  const problemSummary = t(needsPassword ? "workflow.metadata.warning.PasswordUnknown"
    : assessmentDiagnostic ? `cloudDownload.problemCode.${assessmentDiagnostic}` : `${guidancePrefix}.why`);
  // A failed archive probe is executor diagnostics even when AI proposed the step.
  const diagnosticMessage = assessmentExplanation ?? (!pathConflict && !assessmentDiagnostic &&
    typeof prompt.message === "string" && prompt.message.trim() && prompt.message !== prompt.reason
      ? prompt.message : undefined);
  const requiredComponentId = reason === "DEPENDENCY_REQUIRED" ? (() => {
    if (typeof prompt.requiredComponentId === "string" && prompt.requiredComponentId.trim())
      return prompt.requiredComponentId;
    // Older waiting runs predate the runtime dependency descriptor.
    try {
      const savedPlan = JSON.parse(prompt.extractionPlanJson ?? "{}");
      return Array.isArray(savedPlan.steps) && savedPlan.steps.some((step: any) =>
        step.id === prompt.knownStepId && step.op === "extractArchive" && step.zipPasswordCodePage != null)
        ? "pyzipper-component-service" : undefined;
    } catch { return undefined; }
  })() : undefined;
  return (
    <div className={fileTask ? "min-w-0 space-y-3" : "min-w-0 space-y-5"}>
      {diagnosticMessage ? <ProcessingProblemSummary message={diagnosticMessage}
        reason={assessmentDiagnostic || reason} summary={problemSummary} /> : fileTask ? (
        reason !== "ExtractionPlanUnknown" && (
          <p className="break-words text-sm text-warning-700">
            {problemSummary}
          </p>
        )
      ) : <ResumeGuidance
        why={problemSummary}
        action={t(`${guidancePrefix}.action`)}
        next={t(`${guidancePrefix}.next`)}
      />}
      {!fileTask && prompt.title && <p className="text-sm font-medium">{prompt.title}</p>}
      {pathConflict && <ProcessingPathConflictSummary conflict={prompt.pathConflict} />}
      {requiredComponentId && <DependencyManager id={requiredComponentId} mode="summary" />}
      {!fileTask && prompt.deliveryReview && <p className="text-xs text-default-500">{t("cloudDownload.deliveryReview.automaticRule")}</p>}
      {!fileTask && prompt.deliveryReview && Array.isArray(prompt.files) && prompt.files.length > 0 && (
        <ul className="list-disc space-y-1 pl-5 text-sm">
          {prompt.files.slice(0, 8).filter((file): file is string => typeof file === "string")
            .map((file) => <li key={file} className="break-all">{file}</li>)}
        </ul>
      )}
      {prompt.canEditPlan === false && !needsPassword && !retryCurrentStep && (
        <p className="text-sm text-warning-600">{t("acquisition.plan.lockedPlan")}</p>
      )}
      {!fileTask && prompt.adaptiveAssessment && (
        <Textarea
          label={t("cloudDownload.processing.adaptiveUserContext")}
          description={t("cloudDownload.processing.adaptiveUserContextDescription")}
          minRows={3}
          value={userContext}
          onValueChange={setUserContext}
          isDisabled={submitting}
        />
      )}
      {needsPassword && prompt.archiveName && (
        <p className="break-all text-sm">
          {t("cloudDownload.processing.archive", { name: prompt.archiveName })}
        </p>
      )}
      {needsPassword && (
        <p className="text-xs text-default-500">
          {t(
            (prompt.depth ?? 0) > 0
              ? "cloudDownload.processing.recursivePassword"
              : "cloudDownload.processing.currentPassword",
          )}
        </p>
      )}
      {needsBudget && (
        <p className="text-sm text-warning-600">{t("cloudDownload.processing.budgetExceeded")}</p>
      )}
      {repeatedContent && (
        <p className="text-sm text-warning-600">{t("cloudDownload.processing.repeatedContent")}</p>
      )}
      {waitingShared && (
        <p className="text-sm text-default-500">{t("cloudDownload.processing.waitingShared")}</p>
      )}
      {!fileTask && <p className="text-xs text-default-500">{t("cloudDownload.processing.resumeHint")}</p>}
      {needsPassword ? (
        <ArchivePasswordInput
          label={t("workflow.acquisition.unpack.password")}
          value={password}
          onValueChange={setPassword}
          isDisabled={submitting}
        />
      ) : needsPlan && fileTask ? (
        <TaskFilePlanDecision targetId={prompt.targetId} saved={prompt.extractionPlanJson}
          policies={prompt.allowedArchivePolicies} adaptive={prompt.adaptiveAssessment === true}
          pathConflict={pathConflict} submitting={submitting} onSubmit={onSubmit} />
      ) : needsPlan ? (
        <div className="space-y-2">
          {!pathConflict && (
            <>
              <ArchivePolicy
                value={archivePolicy}
                onChange={setArchivePolicy}
                disabled={submitting}
                allowedPolicies={prompt.allowedArchivePolicies}
                keepLabel={prompt.deliveryReview ? t(prompt.reason === "FILE_DELIVERY_RESULT_UNCERTAIN" ? "cloudDownload.deliveryReview.keepCurrentFiles" : "cloudDownload.deliveryReview.keepArchives") : undefined}
              />
              <p className="text-xs text-default-500">
                {t("cloudDownload.processing.policyScopeHint")}
              </p>
              {prompt.reason === "ArchiveInputRequired" && (
                <p className="text-xs text-warning-600">
                  {t("cloudDownload.processing.archiveInputHint")}
                </p>
              )}
              <p className="rounded-lg bg-default-50 p-3 text-sm leading-relaxed text-default-600">
                {t(`cloudDownload.resumeGuide.planChoice.${archivePolicy}`)}
              </p>
            </>
          )}
          {archivePolicy === "instructions" && (
            <div className="space-y-2 border-l-2 border-default-200 pl-3">
              <p className="text-xs font-medium text-default-500">
                {t("cloudDownload.resumeGuide.exactPlan")}
              </p>
              <ProcessingPlanField value={plan} onChange={setPlan} isDisabled={submitting} />
            </div>
          )}
        </div>
      ) : needsBudget ? (
        <RecursiveLimits
          value={recursiveOptions}
          onChange={setRecursiveOptions}
          disabled={submitting}
        />
      ) : null}
      {!(fileTask && needsPlan) && <div className={fileTask ? "flex flex-wrap justify-end gap-2" : "space-y-3 border-t border-default-200 pt-5"}>
        {!fileTask && <p className="text-xs leading-relaxed text-default-500">
          {t("cloudDownload.resumeGuide.processingEffect")}
        </p>}
        {canCheckPostPassword && (
          <Button size="sm" variant="flat" color="primary" isDisabled={submitting}
            onPress={() => onSubmit(JSON.stringify({
              targetId: prompt.targetId, archiveId: prompt.archiveId, action: "tryPostPasswordEvidence",
            }))}>
            {t("workflow.processing.checkPostPassword")}
          </Button>
        )}
        <ResumeActions busy={submitting}><Button
          className="h-auto whitespace-normal px-4 py-2.5"
          color="primary"
          isLoading={submitting}
          isDisabled={submitting || !valid}
          onPress={() =>
            onSubmit(
              JSON.stringify({
                targetId: prompt.targetId,
                action: retryCurrentStep ? "retryCurrentStep" : undefined,
                archiveId: retryCurrentStep ? undefined : prompt.archiveId,
                password: needsPassword ? password : undefined,
                extractionPlanJson:
                  needsPlan && archivePolicy === "instructions" ? plan : undefined,
                archivePolicy:
                  needsPlan && archivePolicy !== "instructions" ? archivePolicy : undefined,
                recursiveOptions: needsBudget ? limitsPayload(recursiveOptions) : undefined,
                userContext: userContext.trim() || undefined,
              }),
            )
          }
        >
          {t(
            retryCurrentStep ? "workflow.fileRecovery.retryCurrentStep" : waitingShared
              ? "cloudDownload.processing.checkShared"
              : "cloudDownload.processing.continue",
          )}
        </Button></ResumeActions>
      </div>}
    </div>
  );
};

function ArchivePolicy({
  value,
  onChange,
  disabled = false,
  allowedPolicies,
  keepLabel,
}: {
  value: ProcessingConfig["archivePolicy"];
  onChange: (value: ProcessingConfig["archivePolicy"]) => void;
  disabled?: boolean;
  allowedPolicies?: ProcessingConfig["archivePolicy"][];
  keepLabel?: string;
}) {
  const { t } = useTranslation();
  return (
    <Select
      label={t("cloudDownload.processing.archivePolicy")}
      selectedKeys={[value]}
      disallowEmptySelection
      isDisabled={disabled}
      dataSource={(["instructions", "autoDetect", "keep"] as const)
        .filter((policy) => !allowedPolicies || allowedPolicies.includes(policy))
        .map((policy) => ({
          value: policy,
          label: policy === "keep" && keepLabel ? keepLabel : t(`cloudDownload.processing.policy.${policy}`),
        }))}
      description={t("cloudDownload.processing.authorizationHint")}
      onSelectionChange={(keys) => {
        const selected = String(Array.from(keys)[0]);
        const policy = (["instructions", "autoDetect", "keep"] as const).find(
          (candidate) =>
            candidate === selected && (!allowedPolicies || allowedPolicies.includes(candidate)),
        );
        if (policy) onChange(policy);
      }}
    />
  );
}

function RecursiveLimits({
  value,
  onChange,
  disabled = false,
}: {
  value: ProcessingConfig["recursive"];
  onChange: (value: ProcessingConfig["recursive"]) => void;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      {(
        [
          "maxDepth",
          "maxArchiveGroups",
          "maxFiles",
          "maxExpandedBytes",
          "minimumFreeBytes",
        ] as const
      ).map((field) => (
        <NumberInput
          key={field}
          label={t(`cloudDownload.processing.${field}`)}
          minValue={field === "minimumFreeBytes" ? 0 : 1}
          step={1}
          isDisabled={disabled}
          value={value[field]}
          onValueChange={(number) => onChange({ ...value, [field]: number })}
        />
      ))}
    </div>
  );
}

const ConfigForm: FC<{
  value: ProcessingConfig;
  onChange: (value: ProcessingConfig) => void;
  descriptionKey?: string;
}> = ({
  value,
  onChange,
  descriptionKey = "cloudDownload.activity.download.processFiles.description",
}) => {
  const { t } = useTranslation();
  return (
    <div className="space-y-3">
      <p className="text-sm text-default-500">{t(descriptionKey)}</p>
      <ReusePolicyField
        value={value.reusePolicy}
        onChange={(reusePolicy) => onChange({ ...value, reusePolicy })}
        processing
      />
      <ArchivePolicy
        value={value.archivePolicy}
        onChange={(archivePolicy) => onChange({ ...value, archivePolicy })}
      />
      {value.archivePolicy !== "keep" && (
        <ExtractionLayoutField
          value={value.extractionLayout}
          onChange={extractionLayout => onChange({ ...value, extractionLayout })}
        />
      )}
      <Switch
        isSelected={value.recursive.enabled}
        onValueChange={(enabled) =>
          onChange({ ...value, recursive: { ...value.recursive, enabled } })
        }
      >
        {t("cloudDownload.processing.recursive")}
      </Switch>
      <p className="text-xs text-default-500">{t("cloudDownload.processing.recursiveHint")}</p>
      {value.recursive.enabled && (
        <RecursiveLimits
          value={value.recursive}
          onChange={(recursive) => onChange({ ...value, recursive })}
        />
      )}
      <Accordion variant="light" className="px-0">
        <AccordionItem key="storage" title={t("workflow.parameters.nodeStorage")}>
          <div className="space-y-3 pb-2">
            <p className="text-xs text-default-500">{t("workflow.parameters.nodeStorageHint")}</p>
            {value.workspaceRootDirectory != null && (
              <div className="space-y-2 rounded-lg border border-warning-200 p-2">
                <p className="text-xs text-warning-600">
                  {t("workflow.parameters.legacyWorkspaceHint")}
                </p>
                <FileSystemSelectorButton
                  key={value.workspaceRootDirectory || "empty-workspace"}
                  className="h-auto min-h-8 max-w-full whitespace-normal break-all text-left"
                  fileSystemSelectorProps={{
                    targetType: "folder",
                    multiple: false,
                    defaultSelectedPath: value.workspaceRootDirectory || undefined,
                    startPath: value.workspaceRootDirectory || undefined,
                    onSelected: (entry) =>
                      entry.path && onChange({ ...value, workspaceRootDirectory: entry.path }),
                  }}
                />
                <Button
                  size="sm"
                  variant="flat"
                  onPress={() =>
                    onChange({
                      ...value,
                      workspaceRootDirectory: null,
                      storagePolicy: {
                        ...(value.storagePolicy ?? defaultStoragePolicy),
                        processingWorkspaceDirectory: value.workspaceRootDirectory || "",
                      },
                    })
                  }
                >
                  {t("workflow.parameters.migrateWorkspace")}
                </Button>
              </div>
            )}
            <Switch
              size="sm"
              isSelected={!!value.storagePolicy}
              onValueChange={(enabled) =>
                onChange({ ...value, storagePolicy: enabled ? { ...defaultStoragePolicy } : null })
              }
            >
              {t("workflow.parameters.customStorage")}
            </Switch>
            {value.storagePolicy ? (
              <StoragePolicyFields
                processingOnly
                showWorkspace
                showHint={false}
                value={value.storagePolicy}
                onChange={(storagePolicy) => onChange({ ...value, storagePolicy })}
              />
            ) : (
              <p className="text-xs text-default-500">{t("workflow.parameters.inheritStorage")}</p>
            )}
          </div>
        </AccordionItem>
      </Accordion>
    </div>
  );
};

export const ProcessCloudFilesUI: WorkflowActivityUI<ProcessingConfig> = {
  kind: "download.processFiles",
  executionLabels: downloadWorksetExecutionLabels,
  displayNameKey: "cloudDownload.activity.download.processFiles.name",
  category: WorkflowActivityCategory.Action,
  defaultConfig: defaultProcessingConfig,
  parseConfig: parseProcessingConfig,
  serializeConfig: JSON.stringify,
  isValid: isValidConfig,
  ConfigForm,
  Summary: ({ config }) => {
    const { t } = useTranslation();
    return (
      <span className="text-xs text-default-500">
        {t(
          config.reusePolicy === "force"
            ? "cloudDownload.reuse.forceProcessing"
            : "cloudDownload.reuse.verified",
        )}
        {config.recursive.enabled && ` · ${t("cloudDownload.processing.recursive")}`}
      </span>
    );
  },
  ResumeForm,
};

/** The same file lifecycle is available for exact local/acquisition manifests. */
export const ProcessFileArtifactsUI: WorkflowActivityUI<ProcessingConfig> = {
  ...ProcessCloudFilesUI,
  kind: "fs.processArtifacts",
  executionLabels: fileProcessingExecutionLabels,
  displayNameKey: "workflow.activity.fsProcessArtifacts.name",
  ConfigForm: (props) => (
    <ConfigForm {...props} descriptionKey="workflow.activity.fsProcessArtifacts.description" />
  ),
};
