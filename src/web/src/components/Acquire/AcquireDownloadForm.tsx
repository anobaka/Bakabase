import { useEffect, useMemo, useRef, useState, type FormEvent } from "react";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, Input, Select } from "@/components/bakaui";
import DownloadDirectoryField from "@/components/ThirdPartyConfig/base/DownloadDirectoryField";
import { useDownloaderGlobalOptionsStore } from "@/stores/options";
import BApi from "@/sdk/BApi";
import { CloudDownloadSources } from "@/sdk/constants";
import MethodPreparation from "./MethodPreparation";
import { requireIntentResult } from "./api";
import type { AcquireAdapter, AcquireRequest } from "./models";
import { useDownloadPreflight } from "./useDownloadPreflight";
import SourceResolution from "./SourceResolution";
import { downloadMethodLabel } from "./methodLabels";
import {
  DownloadMethodAvailabilityNotice,
  isDownloadMethodUnavailable,
} from "@/components/CloudDownload/DownloadMethodAvailability";

const downloadAdapter: AcquireAdapter = {
  preflight: (request, directory) =>
    requireIntentResult(
      BApi.downloadCenter.preflightDownloadCenterCloud(request, { outputDirectory: directory }),
    ),
  submit: (request, directory) =>
    requireIntentResult(
      BApi.downloadCenter.createDownloadCenterCloudTasks(request, { outputDirectory: directory }),
    ),
};

/** Embedded in the download-center modal; acquisition goals and workflow selection are deliberately absent. */
export default function AcquireDownloadForm({
  providerId,
  formId,
  onStateChange,
  onCreated,
}: {
  providerId: string;
  formId: string;
  onStateChange: (state: { canSubmit: boolean; busy: boolean }) => void;
  onCreated: () => void;
}) {
  const { t } = useTranslation();
  const defaultDirectory = useDownloaderGlobalOptionsStore(
    (state) => state.data?.defaultDownloadPath,
  );
  const [directory, setDirectory] = useState(defaultDirectory ?? "");
  const [url, setUrl] = useState("");
  const [title, setTitle] = useState("");
  const [accessCode, setAccessCode] = useState("");
  const [accountId, setAccountId] = useState<string>();
  const [remote, setRemote] = useState(false);
  const [methodId, setMethodId] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const locked = useRef(false);
  const request = useMemo<AcquireRequest>(
    () => ({
      mode: "files",
      title: title.trim() || undefined,
      candidates: [
        {
          id: "download",
          providerId,
          methodId,
          accountId,
          kind: remote ? "remote" : "share",
          url: url.trim(),
          accessCode: accessCode.trim() || undefined,
        },
      ],
      selectedCandidateIds: ["download"],
    }),
    [providerId, remote, url, accessCode, title, accountId, methodId],
  );
  const {
    result,
    previousResult,
    error: preflightError,
    refreshing,
    refresh,
    revision,
  } = useDownloadPreflight(downloadAdapter, request, directory.trim());
  const candidate = (result ?? previousResult)?.candidates[0];
  const methods = candidate?.methods ?? [];
  const source = CloudDownloadSources.find((entry) => entry.providerId === providerId);
  const selectedMethodId = methodId ?? candidate?.selectedMethodId ?? methods[0]?.id;
  const selectedMethods = methods.filter((method) => method.id === selectedMethodId);
  const requirements = [
    ...(candidate?.requirements ?? []),
    ...selectedMethods.flatMap((method) => method.requirements),
  ].filter(
    (item, index, values) =>
      values.findIndex((other) => other.code === item.code && other.message === item.message) ===
      index,
  );
  const canSubmit =
    !!result?.canSubmit &&
    !!url.trim() &&
    !busy &&
    !isDownloadMethodUnavailable({ providerId, methodId: selectedMethodId });
  useEffect(() => {
    onStateChange({ canSubmit, busy });
  }, [canSubmit, busy, onStateChange]);
  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (locked.current || !canSubmit) return;
    locked.current = true;
    setBusy(true);
    setError(undefined);
    try {
      await downloadAdapter.submit(request, directory.trim());
      onCreated();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure));
      refresh();
    } finally {
      locked.current = false;
      setBusy(false);
    }
  };
  return (
    <form id={formId} className="space-y-3" onSubmit={(event) => void submit(event)}>
      <fieldset disabled={busy} className="contents">
        <DownloadMethodAvailabilityNotice providerId={providerId} methodId={selectedMethodId} />
        <p className="text-xs text-default-500">{t("acquire.download.hint")}</p>
        {CloudDownloadSources.find((source) => source.providerId === providerId)
          ?.supportsRemote && (
          <Checkbox
            isDisabled={busy}
            isSelected={remote}
            onValueChange={(value) => {
              setRemote(value);
              setUrl("");
              setAccessCode("");
              setAccountId(undefined);
              setMethodId(
                value
                  ? source?.methods.find((method) => method.sourceKinds.includes("remote"))?.id
                  : undefined,
              );
            }}
          >
            {t("acquire.download.remote")}
          </Checkbox>
        )}
        <Input
          label={t(remote ? "acquire.download.remotePath" : "cloudDownload.url")}
          placeholder={remote ? "/folder/file.zip" : "https://…"}
          description={t(remote ? "acquire.download.remoteHint" : "acquire.download.chooseHint")}
          value={url}
          onValueChange={setUrl}
          isDisabled={busy}
          isRequired
        />
        {!remote && (
          <Input
            label={t("cloudDownload.accessCode")}
            value={accessCode}
            onValueChange={setAccessCode}
            isDisabled={busy}
          />
        )}
        <Input label={t("bt.add.name")} value={title} onValueChange={setTitle} isDisabled={busy} />
        <DownloadDirectoryField
          label={t("bt.outputDirectory")}
          value={directory}
          onChange={(value) => {
            if (!locked.current) setDirectory(value);
          }}
        />
        <div className="flex flex-wrap items-center justify-between gap-2 text-xs">
          {url.trim() && candidate && (
            <span>
              {t(`acquire.readiness.${candidate.state}`, { defaultValue: candidate.state })}{" "}
              {selectedMethods.map((method) => downloadMethodLabel(method, t)).join(" · ")}
            </span>
          )}
          <Button
            type="button"
            size="sm"
            variant="light"
            isDisabled={busy}
            isLoading={refreshing}
            onPress={refresh}
          >
            {t("acquire.preparation.refresh")}
          </Button>
        </div>
        <SourceResolution source={candidate?.resolvedSource} />
        {methods.length > 1 && (
          <Select
            label={t("acquire.method.label")}
            isDisabled={busy}
            selectedKeys={selectedMethodId ? [selectedMethodId] : []}
            disallowEmptySelection
            dataSource={methods.map((method) => ({
              value: method.id,
              label: downloadMethodLabel(method, t),
            }))}
            onSelectionChange={(keys) => {
              setMethodId(String(Array.from(keys)[0]));
              setAccountId(undefined);
            }}
          />
        )}
        {selectedMethods.map((method) => (
          <div key={method.id} className="text-xs text-default-500">
            {method.engineId && <p>{t("acquire.method.engine", { engine: method.engineId })}</p>}
            {method.scope && <p>{t(`acquire.methodScope.${method.scope}`)}</p>}
          </div>
        ))}
        {!!url.trim() && requirements.length > 0 && (
          <ul className="space-y-1 text-xs text-warning-600">
            {requirements.map((requirement, index) => (
              <li key={`${requirement.code}:${index}`}>
                {t(`acquire.requirement.${requirement.code}`, {
                  defaultValue: requirement.message,
                })}
              </li>
            ))}
          </ul>
        )}
        {result?.requirements.map((requirement) => (
          <p key={requirement.code} className="text-xs text-warning-600">
            {t(`acquire.requirement.${requirement.code}`, { defaultValue: requirement.message })}
          </p>
        ))}
        <MethodPreparation
          methods={selectedMethods}
          requirements={requirements}
          accountId={accountId}
          onAccountChange={setAccountId}
          onRefresh={refresh}
          refreshKey={revision}
          isDisabled={busy}
        />
        {refreshing && (
          <p role="status" className="text-xs text-default-500">
            {t("acquire.preparation.checking")}
          </p>
        )}
        {(error || preflightError) && (
          <p role="alert" className="text-sm text-danger">
            {error || preflightError}
          </p>
        )}
        <p className="text-xs text-default-500">{t("acquire.preparation.recheck")}</p>
      </fieldset>
    </form>
  );
}
