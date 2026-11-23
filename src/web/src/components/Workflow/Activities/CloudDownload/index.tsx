import type { WorkflowActivityUI } from "../types";
import { downloadWorksetExecutionLabels } from "../executionLabels";
import { useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import { useTranslation } from "react-i18next";
import {
  Accordion,
  AccordionItem,
  Button,
  Checkbox,
  Input,
  Select,
  Switch,
} from "@/components/bakaui";
import { CloudDownloadSources, WorkflowActivityCategory } from "@/sdk/constants";
import AccountSelect from "@/components/CloudDownload/AccountSelect";
import DownloadDirectoryField from "@/components/ThirdPartyConfig/base/DownloadDirectoryField";
import MethodPreparation from "@/components/Acquire/MethodPreparation";
import { localStorageProblemCode } from "@/components/CloudDownload/problemLabels";
import type { DownloadMethod } from "@/components/Acquire/models";
import { downloadMethodLabel } from "@/components/Acquire/methodLabels";
import AcquisitionSourceDetails, {
  type AccessCodeRevision,
} from "@/components/Acquire/AcquisitionSourceDetails";
import DownloadToolProxies from "@/components/ProxySelector/DownloadToolProxies";
import { dependencyManagerPath } from "@/components/Dependency/routes";
import ResumeActions from "../../ResumeActions";
import { ResumeSubmissionContext } from "../../ResumeSubmissionContext";
import {
  ResumeDetails,
  ResumeGuidance,
  ResumeProblem,
  useResumeActivityKind,
} from "../../ResumePresentation";
import { ProcessCloudFilesUI } from "./ProcessFiles";
import { originalDownloadId, useDownloadResumeTargets } from "./useDownloadResumeTargets";
import { CleanupArtifactsUI } from "./CleanupArtifacts";
import ReusePolicyField, { isReusePolicy, type ReusePolicy } from "./ReusePolicy";
import StoragePolicyFields, {
  defaultStoragePolicy,
  storagePolicyIsInvalid,
  type StoragePolicy,
} from "../../ArtifactStorage/PolicyFields";

export interface CloudConfig {
  storagePolicy?: StoragePolicy | null;
  accountId?: string;
  remoteDirectory?: string;
  downloadLayout?: "currentDirectory" | "separateDirectory";
  outputDirectory?: string;
  sourceIds?: string[];
  preferProviders?: string[];
  allowUngrouped?: boolean;
  reusePolicy?: ReusePolicy;
}
export interface CloudTarget {
  id: string;
  excluded?: boolean;
  title?: string;
  selectedSourceId?: string;
  accountId?: string;
  problem?: string;
  transferDiagnostic?: { code: string; providerCode?: number | null; itemCodes?: number[]; fileCount?: number | null; fileLimit?: number | null };
  downloadTaskId?: number;
  transportTaskId?: string;
  downloadOperationId?: string;
  downloadArtifactId?: string;
  processedArtifactId?: string;
  publishedArtifactId?: string;
  remoteEntry?: unknown;
  files?: string[];
  sources: {
    id: string;
    url: string;
    originalUrl?: string;
    providerId?: string;
    kind?: string;
    methodId?: string;
    accountId?: string;
    accessCode?: string;
    accessCodeRevisions?: AccessCodeRevision[];
    archivePassword?: string;
    groupId?: string;
  }[];
}
export interface CloudPrompt {
  reason?: string;
  targets?: CloudTarget[];
  providers?: { providerId: string; methodId?: string; available: boolean; problem?: string }[];
}

export function parseCloudPrompt(json: string | null): CloudPrompt {
  try {
    const value: unknown = JSON.parse(json ?? "{}");
    return value && typeof value === "object" && !Array.isArray(value)
      ? (value as CloudPrompt)
      : {};
  } catch {
    return {};
  }
}

/** Every selected target has one explicit mirror. Choosing another mirror replaces its selection. */
export function toggleCloudSource(
  previous: string[],
  targets: CloudTarget[],
  targetId: string,
  sourceId: string,
  checked: boolean,
) {
  const siblings = new Set(
    targets.find((target) => target.id === targetId)?.sources.map((source) => source.id) ?? [],
  );
  return checked
    ? [...previous.filter((id) => !siblings.has(id)), sourceId]
    : previous.filter((id) => id !== sourceId);
}

const CloudResumeForm: NonNullable<WorkflowActivityUI<CloudConfig>["ResumeForm"]> = ({
  runId,
  promptJson,
  submitting,
  onSubmit,
  fileTask = false,
}) => {
  const { t } = useTranslation();
  const activityKind = useResumeActivityKind();
  const prompt = useMemo(() => parseCloudPrompt(promptJson), [promptJson]);
  const targets = useMemo(() => Array.isArray(prompt.targets) ? prompt.targets : [], [prompt]);
  const canChooseSources = prompt.reason === "chooseSources";
  const isTransfer = activityKind === "baidu.transfer";
  const isAwait = activityKind === "download.awaitCompletion";
  const owner = useContext(ResumeSubmissionContext);
  const selectOriginals = isAwait && (targets.length > 1 ||
    owner?.runId === runId && owner?.downloadTargets?.some(target => target.downloadId?.startsWith("bt:")) === true);
  const downloadTargets = useDownloadResumeTargets(runId, targets, selectOriginals);
  const [selectedTargets, setSelectedTargets] = useState<string[]>([]);
  const liveTargets = new Map(downloadTargets?.map(row => [row.targetId, row]));
  const currentDownload = (target: CloudTarget) => {
    const live = liveTargets.get(target.id);
    return live?.downloadId === originalDownloadId(target) ? live : undefined;
  };
  const canRetryTarget = (target: CloudTarget) => {
    const live = currentDownload(target);
    return !target.excluded && !target.downloadArtifactId && !target.processedArtifactId && !target.publishedArtifactId &&
      live?.canRetry === true && ["failed", "stopped"].includes(live.status);
  };
  const resumeTargetIds = targets.filter(target => selectedTargets.includes(target.id) && canRetryTarget(target)).map(target => target.id);
  const canObserve = selectOriginals && targets.some(target => !target.excluded) && targets.every(target =>
    target.excluded || target.downloadArtifactId || target.processedArtifactId || target.publishedArtifactId ||
      ["completed", "running", "queued"].includes(currentDownload(target)?.status ?? ""));
  const problems = [
    ...targets.map((target) => target.problem),
    ...(prompt.providers ?? []).map((provider) => provider.problem),
  ]
    .filter(Boolean)
    .join("\n");
  const originalUnavailable = problems.includes("PIKPAK_ORIGINAL_FILE_UNAVAILABLE");
  const approvingUnavailableOriginal = selectOriginals
    ? targets.some(target => resumeTargetIds.includes(target.id) &&
        (currentDownload(target)?.problem || target.problem)?.includes("PIKPAK_ORIGINAL_FILE_UNAVAILABLE"))
    : originalUnavailable;
  const accountProblem = /ACCOUNT|LOGIN|CREDENTIAL|SESSION|Choose a Baidu account/i.test(problems);
  const dependencyProblem = /DEPENDENCY|COMPONENT|TOOL_NOT_READY|NOT_INSTALLED/i.test(problems);
  const needsResolverNetwork = problems.includes("SOURCE_NETWORK_REQUIRES_PROXY");
  const localStorageProblem = localStorageProblemCode(problems);
  const guide = originalUnavailable
    ? "originalUnavailable"
    : canChooseSources
      ? "chooseSources"
      : prompt.reason === "chooseDirectory"
        ? "directory"
        : localStorageProblem
          ? localStorageProblem === "LOCAL_STORAGE_RESERVE_LOW" ? "storageReserve" : "localStorage"
          : accountProblem
            ? "account"
            : dependencyProblem
              ? "dependency"
              : isAwait
                ? "downloadStopped"
                : "source";
  const [selected, setSelected] = useState<string[]>(
    targets.flatMap((target) => (target.selectedSourceId ? [target.selectedSourceId] : [])),
  );
  const [sourceMethods, setSourceMethods] = useState<Record<string, string>>({});
  const [sourceAccounts, setSourceAccounts] = useState<Record<string, string>>({});
  const [revision, setRevision] = useState(0);
  const [outputDirectory, setOutputDirectory] = useState("");
  const [retryOpen, setRetryOpen] = useState(false);
  const [retryApproved, setRetryApproved] = useState(false);
  useEffect(() => {
    setSelected(
      targets.flatMap((target) => (target.selectedSourceId ? [target.selectedSourceId] : [])),
    );
    setSourceMethods({});
    setSourceAccounts({});
    setOutputDirectory("");
    setRetryApproved(false);
    setRetryOpen(false);
  }, [promptJson, activityKind]);
  useEffect(() => {
    setSelectedTargets([]);
    setRetryApproved(false);
  }, [downloadTargets, promptJson, activityKind]);
  const needsApproval = originalUnavailable || isTransfer || isAwait;
  const submitAnswer = () => {
    if (selectOriginals) {
      onSubmit(JSON.stringify(canObserve ? { observeExistingOnly: true } : { observeExistingOnly: true, resumeTargetIds }));
      return;
    }
    onSubmit(JSON.stringify({
      sourceIds: canChooseSources ? selected : undefined,
      sourceMethods: Object.keys(sourceMethods).length ? sourceMethods : undefined,
      sourceAccounts: Object.keys(sourceAccounts).length ? sourceAccounts : undefined,
      outputDirectory: outputDirectory.trim() || undefined,
    }));
  };
  const canContinue =
    !submitting &&
    (!canChooseSources || selected.length > 0) &&
    (prompt.reason !== "chooseDirectory" || !!outputDirectory.trim()) &&
    (!needsApproval || canObserve || retryApproved) &&
    (!selectOriginals || canObserve || resumeTargetIds.length > 0);
  return (
    <div className={fileTask ? "min-w-0 space-y-3" : "min-w-0 space-y-5"}>
      {fileTask ? !targets.some(target => target.problem) && (
        problems ? <ResumeProblem problem={problems} fallback={t(`cloudDownload.resumeGuide.${guide}.why`)} /> :
          <p className="break-words text-sm text-warning-700">{t(`cloudDownload.resumeGuide.${guide}.why`)}</p>
      ) : <ResumeGuidance
        why={t(`cloudDownload.resumeGuide.${guide}.why`)}
        action={t(`cloudDownload.resumeGuide.${guide}.action`)}
        next={t(`cloudDownload.resumeGuide.${guide}.next`)}
      />}
      {selectOriginals && !canObserve && <p className="text-sm text-default-600">{t("cloudDownload.resumeGuide.chooseRetryTargets")}</p>}
      <div className="space-y-4">
        {targets.map((target, targetIndex) => {
          const title = target.title || t("cloudDownload.resumeGuide.content", { number: targetIndex + 1 });
          const live = selectOriginals ? currentDownload(target) : undefined;
          const status = target.downloadArtifactId || target.processedArtifactId || target.publishedArtifactId ? "completed" : live?.status;
          const problem = !selectOriginals || !status || ["failed", "stopped", "unavailable"].includes(status)
            ? live?.problem || target.problem : undefined;
          const report = target.transferDiagnostic?.code === problem && problem?.startsWith("BAIDU_TRANSFER_")
            ? target.transferDiagnostic : undefined;
          const itemCodes = Array.isArray(report?.itemCodes)
            ? report.itemCodes.filter(code => Number.isInteger(code) && Math.abs(code) <= 999999).slice(0, 8) : [];
          return <section
            key={target.id}
            className={fileTask ? "min-w-0 space-y-3" : "min-w-0 space-y-3 rounded-xl border border-default-200 p-4"}
          >
            {selectOriginals ? (
              <div className="flex min-w-0 flex-wrap items-center gap-x-3 gap-y-1">
                <Checkbox isDisabled={submitting || !canRetryTarget(target)}
                  isSelected={resumeTargetIds.includes(target.id)}
                  onValueChange={checked => {
                    setSelectedTargets(ids => checked ? [...ids, target.id] : ids.filter(id => id !== target.id));
                    setRetryApproved(false);
                  }}>
                  <span className="break-words text-sm font-medium">{title}{target.downloadTaskId != null && ` · #${target.downloadTaskId}`}</span>
                </Checkbox>
                <span className="text-xs text-default-500">
                  {status && status !== "unavailable" ? t(`downloadCenter.status.${status}`, {defaultValue: t("cloudDownload.resumeGuide.unknownDownloadState")})
                    : t("cloudDownload.resumeGuide.unknownDownloadState")}
                </span>
              </div>
            ) : <h3 className="break-words text-sm font-medium">{title}</h3>}
            {problem && (
              <ResumeProblem
                problem={problem}
                fallback={t(`cloudDownload.resumeGuide.${guide}.why`)}
              />
            )}
            {report && typeof report.providerCode === "number" && Number.isInteger(report.providerCode) && Math.abs(report.providerCode) <= 999999 && (
              <div className="text-xs text-default-600">
                <p>{t("cloudDownload.transferReport.providerCode", { code: report.providerCode })}</p>
                {!!itemCodes?.length && <p>{t("cloudDownload.transferReport.itemCodes", { codes: itemCodes.join(", ") })}</p>}
                {report.fileCount != null && report.fileLimit != null && Number.isInteger(report.fileCount) && Number.isInteger(report.fileLimit)
                  && report.fileCount > report.fileLimit && report.fileLimit > 0 && report.fileCount <= 100000000 &&
                  <p>{t("cloudDownload.transferReport.countLimit", { count: report.fileCount, limit: report.fileLimit })}</p>}
              </div>
            )}
            {(target.transportTaskId || target.downloadTaskId != null) && (
              <Button
                as="a"
                href={`#/downloader?id=${encodeURIComponent(target.transportTaskId ?? `site:${target.downloadTaskId}`)}`}
                target="_blank"
                rel="noopener noreferrer"
                size="sm"
                variant="flat"
                color="primary"
                className="h-auto max-w-full whitespace-normal px-3 py-2"
              >
                {t("cloudDownload.resumeGuide.openTask")}
              </Button>
            )}
            {target.sources.map((source) => {
              const descriptor = CloudDownloadSources.find(
                (item) => item.providerId === source.providerId,
              );
              const choices =
                descriptor?.methods.filter((method) =>
                  method.sourceKinds.includes(source.kind === "remote" ? "remote" : "share"),
                ) ?? [];
              const methodId = sourceMethods[source.id] ?? source.methodId ?? choices[0]?.id;
              const method = choices.find((item) => item.id === methodId);
              const selectedMethod: DownloadMethod[] = method
                ? [
                    {
                      ...method,
                      providerId: source.providerId!,
                      transport: "site",
                      supported: true,
                      ready: false,
                      state: "needsSetup",
                      requirements: [],
                      supportsTransfer: false,
                      dependencyIds: [...method.dependencyIds],
                      sourceKinds: [...method.sourceKinds],
                      engineId: method.engineId ?? undefined,
                      accountProvider: method.accountProvider ?? undefined,
                      scope: method.scope ?? undefined,
                    },
                  ]
                : [];
              const locked =
                submitting ||
                target.downloadTaskId != null ||
                !!target.transportTaskId ||
                !!target.downloadOperationId ||
                !!target.downloadArtifactId ||
                !!target.processedArtifactId ||
                !!target.publishedArtifactId ||
                !!target.remoteEntry;
              return (
                <div key={source.id} className="min-w-0 space-y-3">
                  {canChooseSources ? (
                    <Checkbox
                      aria-label={source.url}
                      isDisabled={submitting}
                      isSelected={selected.includes(source.id)}
                      onValueChange={(checked) =>
                        setSelected((previous) =>
                          toggleCloudSource(previous, targets, target.id, source.id, checked),
                        )
                      }
                    >
                      <span className="break-all text-sm">
                        {method
                          ? downloadMethodLabel(method, t)
                          : source.providerId || t("cloudDownload.resumeGuide.sourceLabel")}
                      </span>
                    </Checkbox>
                  ) : (
                    <p className="text-xs text-default-500">
                      {t(
                        target.selectedSourceId && source.id !== target.selectedSourceId
                          ? "acquisition.sourceDetails.alternativeSource"
                          : "cloudDownload.resumeGuide.currentSource",
                      )}
                      :{" "}
                      {method
                        ? downloadMethodLabel(method, t)
                        : source.providerId || t("cloudDownload.resumeGuide.sourceLabel")}
                    </p>
                  )}
                  <AcquisitionSourceDetails source={source} />
                  {selected.includes(source.id) && (
                    <SourcePreparation collapsed={fileTask && locked && !accountProblem && !dependencyProblem}>
                      {choices.length > 1 && (
                        <Select
                          label={t("acquire.method.label")}
                          selectedKeys={methodId ? [methodId] : []}
                          isDisabled={locked}
                          disallowEmptySelection
                          dataSource={choices.map((choice) => ({
                            value: choice.id,
                            label: downloadMethodLabel(choice, t),
                          }))}
                          onSelectionChange={(keys) => {
                            setSourceMethods((current) => ({
                              ...current,
                              [source.id]: String(Array.from(keys)[0]),
                            }));
                            setSourceAccounts((current) => ({ ...current, [source.id]: "" }));
                          }}
                        />
                      )}
                      <MethodPreparation
                        methods={selectedMethod}
                        requirements={[]}
                        accountId={
                          sourceAccounts[source.id] ?? source.accountId ?? target.accountId
                        }
                        onAccountChange={(id) => {
                          if (!locked)
                            setSourceAccounts((current) => ({ ...current, [source.id]: id ?? "" }));
                        }}
                        onRefresh={() => setRevision((value) => value + 1)}
                        refreshKey={revision}
                        isDisabled={submitting}
                        accountSelectionDisabled={locked}
                      />
                      {locked && (
                        <p className="text-xs text-default-500">
                          {t("cloudDownload.resumeGuide.boundSource")}
                        </p>
                      )}
                    </SourcePreparation>
                  )}
                </div>
              );
            })}
          </section>;
        })}
      </div>
      {needsResolverNetwork && (
        <div className={fileTask ? "space-y-2" : "space-y-2 rounded-xl border border-default-200 p-4"}>
          <p className="text-sm font-medium">{t("cloudDownload.resumeGuide.network")}</p>
          <DownloadToolProxies toolIds={["sourceResolver"]} />
        </div>
      )}
      {!isTransfer && !isAwait && !originalUnavailable && (
        <DownloadDirectoryField
          value={outputDirectory}
          onChange={setOutputDirectory}
          label={t("bt.outputDirectory")}
          description={t("cloudDownload.resume.keepDirectory")}
        />
      )}
      <ResumeDetails title={t("cloudDownload.resumeGuide.optionalSettings")}>
        <p className="text-xs text-default-500">
          {t(
            originalUnavailable
              ? "cloudDownload.resumeGuide.settingsNotFix"
              : "cloudDownload.resumeGuide.settingsHint",
          )}
        </p>
        <div className="flex flex-col items-stretch gap-3 sm:flex-row sm:flex-wrap sm:items-center">
          <Button
            as="a"
            href={`#${dependencyManagerPath()}`}
            target="_blank"
            rel="noopener noreferrer"
            size="sm"
            variant="bordered"
            className="h-auto whitespace-normal px-3 py-2"
          >
            {t("cloudDownload.dependencies")}
          </Button>
        </div>
        {!!prompt.providers?.length && (
          <div>
            {prompt.providers.map((provider) => (
              <p key={provider.methodId ?? provider.providerId}>
                {provider.methodId || provider.providerId}:{" "}
                {t(provider.available ? "cloudDownload.available" : "cloudDownload.unavailable")}
              </p>
            ))}
          </div>
        )}
      </ResumeDetails>
      <section className={fileTask ? "space-y-3" : "space-y-3 border-t border-default-200 pt-5"}>
        {!fileTask && <p className="text-sm font-medium">{t("cloudDownload.resumeGuide.continueTitle")}</p>}
        <p className="text-xs leading-relaxed text-default-500">
          {t(
            canObserve
              ? "cloudDownload.resumeGuide.observeEffect"
              : isTransfer
                ? "cloudDownload.resumeGuide.transferEffect"
                : isAwait
                  ? "cloudDownload.resumeGuide.downloadRetryEffect"
                  : "cloudDownload.resumeGuide.continueEffect",
          )}
        </p>
        {approvingUnavailableOriginal && (
          <Button size="sm" variant="light" onPress={() => setRetryOpen((value) => !value)}>
            {t("cloudDownload.resumeGuide.advancedRetry")}
          </Button>
        )}
        {needsApproval && !canObserve && (!approvingUnavailableOriginal || retryOpen) && (
          <Checkbox
            isDisabled={submitting || selectOriginals && !resumeTargetIds.length}
            isSelected={retryApproved}
            onValueChange={setRetryApproved}
          >
            <span className="text-sm">
              {t(
                approvingUnavailableOriginal
                  ? "cloudDownload.resumeGuide.confirmUnavailableRetry"
                  : selectOriginals
                    ? "cloudDownload.resumeGuide.confirmSelectedDownloads"
                    : isTransfer
                      ? "cloudDownload.resumeGuide.confirmTransfer"
                      : "cloudDownload.resumeGuide.confirmDownloadRetry",
              )}
            </span>
          </Checkbox>
        )}
        <ResumeActions busy={submitting}>
          <Button
            color="primary"
            className="h-auto whitespace-normal px-4 py-2.5"
            isLoading={submitting}
            isDisabled={!canContinue}
            onPress={submitAnswer}
          >
            {t(
              canObserve ? "cloudDownload.resumeGuide.observeDownloads" : isTransfer
                ? "cloudDownload.resumeGuide.retryTransfer"
                : isAwait
                  ? "cloudDownload.resumeGuide.retryDownload"
                  : "cloudDownload.resume.continue",
            )}
          </Button>
        </ResumeActions>
      </section>
    </div>
  );
};

function SourcePreparation({ collapsed, children }: { collapsed: boolean; children: ReactNode }) {
  const { t } = useTranslation();
  return collapsed ? <ResumeDetails title={t("cloudDownload.resumeGuide.preparation")}>{children}</ResumeDetails>
    : <div className="space-y-3">{children}</div>;
}

function activity(kind: string, defaults: CloudConfig = {}): WorkflowActivityUI<CloudConfig> {
  const isSelection = kind === "download.selectSources";
  const isTransfer = kind === "baidu.transfer";
  const isDownload =
    kind === "baidu.download" ||
    kind === "download.enqueueSupported" ||
    kind === "acquisition.fetchSupported";
  const needsOutputDirectory = isDownload || kind === "download.prepareSources";
  const ConfigForm: WorkflowActivityUI<CloudConfig>["ConfigForm"] = ({ value, onChange }) => {
    const { t } = useTranslation();
    return (
      <div className="space-y-3">
        <p className="text-xs text-default-500">
          {t(`cloudDownload.activity.${kind}.description`)}
        </p>
        {(isTransfer || isDownload) && (
          <AccountSelect
            value={value.accountId}
            onChange={(accountId) => onChange({ ...value, accountId })}
          />
        )}
        {isDownload && (
          <ReusePolicyField
            value={value.reusePolicy}
            onChange={(reusePolicy) => onChange({ ...value, reusePolicy })}
          />
        )}
        {isTransfer && (
          <>
            <Input
              label={t("cloudDownload.remoteDirectory")}
              value={value.remoteDirectory ?? "/Bakabase"}
              onValueChange={(remoteDirectory) => onChange({ ...value, remoteDirectory })}
            />
            <Select
              label={t("cloudDownload.downloadLayout.label")}
              description={t("cloudDownload.downloadLayout.description")}
              selectedKeys={[value.downloadLayout ?? "currentDirectory"]}
              disallowEmptySelection
              dataSource={[
                { value: "currentDirectory", label: t("cloudDownload.downloadLayout.current") },
                { value: "separateDirectory", label: t("cloudDownload.downloadLayout.separate") },
              ]}
              onSelectionChange={(keys) => {
                const layout = Array.from(keys)[0];
                if (layout === "currentDirectory" || layout === "separateDirectory")
                  onChange({ ...value, downloadLayout: layout });
              }}
            />
          </>
        )}
        {needsOutputDirectory && (
          <DownloadDirectoryField
            label={t("bt.outputDirectory")}
            value={value.outputDirectory ?? ""}
            onChange={(outputDirectory) => onChange({ ...value, outputDirectory })}
            description={t("cloudDownload.resume.keepDirectory")}
          />
        )}
        {isSelection && (
          <>
            <Input
              label={t("cloudDownload.preferProviders")}
              placeholder={CloudDownloadSources.map((source) => source.providerId).join(",")}
              value={(value.preferProviders ?? []).join(",")}
              onValueChange={(raw) =>
                onChange({
                  ...value,
                  preferProviders: raw
                    .split(",")
                    .map((provider) => provider.trim())
                    .filter(Boolean),
                })
              }
            />
            <Switch
              isSelected={!!value.allowUngrouped}
              onValueChange={(allowUngrouped) => onChange({ ...value, allowUngrouped })}
            >
              {t("cloudDownload.allowUngrouped")}
            </Switch>
            <Accordion variant="light" className="px-0">
              <AccordionItem key="storage" title={t("workflow.parameters.nodeStorage")}>
                <div className="space-y-3 pb-2">
                  <p className="text-xs text-default-500">
                    {t("workflow.parameters.selectedSourceStorageHint")}
                  </p>
                  <Switch
                    size="sm"
                    isSelected={!!value.storagePolicy}
                    onValueChange={(enabled) =>
                      onChange({
                        ...value,
                        storagePolicy: enabled ? { ...defaultStoragePolicy } : null,
                      })
                    }
                  >
                    {t("workflow.parameters.customStorage")}
                  </Switch>
                  {value.storagePolicy ? (
                    <StoragePolicyFields
                      showHint={false}
                      value={value.storagePolicy}
                      onChange={(storagePolicy) => onChange({ ...value, storagePolicy })}
                    />
                  ) : (
                    <p className="text-xs text-default-500">
                      {t("workflow.parameters.inheritStorage")}
                    </p>
                  )}
                </div>
              </AccordionItem>
            </Accordion>
          </>
        )}
      </div>
    );
  };
  return {
    kind,
    executionLabels: downloadWorksetExecutionLabels,
    displayNameKey: `cloudDownload.activity.${kind}.name`,
    category:
      kind === "download.prepareSources"
        ? WorkflowActivityCategory.Transform
        : WorkflowActivityCategory.Action,
    defaultConfig: () => ({
      ...(isDownload ? { reusePolicy: "reuseVerified" as const } : {}),
      ...defaults,
    }),
    parseConfig: (json) => {
      try {
        return {
          ...(isDownload ? { reusePolicy: "reuseVerified" as const } : {}),
          ...defaults,
          ...JSON.parse(json || "{}"),
        };
      } catch {
        return { ...(isDownload ? { reusePolicy: "reuseVerified" as const } : {}), ...defaults };
      }
    },
    serializeConfig: JSON.stringify,
    isValid: (config) =>
      (!config.storagePolicy || !storagePolicyIsInvalid(config.storagePolicy)) &&
      (!isTransfer || (!!config.remoteDirectory?.startsWith("/") &&
        (config.downloadLayout == null || config.downloadLayout === "currentDirectory" || config.downloadLayout === "separateDirectory"))) &&
      (!isDownload || config.reusePolicy == null || isReusePolicy(config.reusePolicy)),
    ConfigForm,
    Summary: ({ config }) => {
      const { t } = useTranslation();
      return (
        <span className="truncate text-xs text-default-500">
          {isTransfer ? config.remoteDirectory : config.outputDirectory}
          {isDownload && (
            <span className="ml-2">
              {t(
                config.reusePolicy === "force"
                  ? "cloudDownload.reuse.forceDownload"
                  : "cloudDownload.reuse.verified",
              )}
            </span>
          )}
        </span>
      );
    },
    ResumeForm: CloudResumeForm,
  };
}

export const cloudDownloadActivities = [
  activity("download.prepareSources"),
  activity("download.selectSources", {
    preferProviders: ["baidu", "googleDrive", "gofile"],
    allowUngrouped: false,
  }),
  activity("baidu.transfer", { remoteDirectory: "/Bakabase" }),
  activity("baidu.download"),
  activity("download.enqueueSupported"),
  activity("download.awaitCompletion"),
  ProcessCloudFilesUI,
  CleanupArtifactsUI,
];

/** Acquisition uses the same durable transfer/download implementation and resume protocol. */
export const AcquisitionFetchSupportedUI = activity("acquisition.fetchSupported");
