import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Chip, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import DeliveryContentLayoutFields, {
  defaultDeliveryContentLayout,
  deliveryContentLayoutsEqual,
  type DeliveryContentLayout,
} from "./DeliveryContentLayoutFields";

type Preview = NonNullable<
  Awaited<ReturnType<typeof BApi.workflow.previewArtifactDeliveryLayout>>["data"]
>;

/** Ledger-only preview. No changes are saved, even when the user changes the preview policy. */
export default function DeliveryLayoutPreview({
  runId,
  artifactId,
  policy: callerPolicy,
  expectedFingerprint,
  onReviewed,
  disabled,
}: {
  runId: number;
  artifactId: string;
  policy?: DeliveryContentLayout;
  expectedFingerprint?: string;
  onReviewed?: (proof?: { projectionFingerprint: string; changed: boolean }) => void;
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  const [draftPolicy, setPolicy] = useState(defaultDeliveryContentLayout);
  const policy = callerPolicy ?? draftPolicy;
  const [preview, setPreview] = useState<Preview>();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(false);
  const [stale, setStale] = useState(false);
  const sequence = useRef(0);
  useEffect(() => {
    sequence.current++;
    setPreview(undefined);
    setBusy(false);
    setError(false);
    setStale(false);
    return () => {
      sequence.current++;
    };
  }, [runId, artifactId, expectedFingerprint]);

  const load = async () => {
    const request = ++sequence.current;
    onReviewed?.();
    setBusy(true);
    setError(false);
    setPreview(undefined);
    setStale(false);
    try {
      const response = await BApi.workflow.previewArtifactDeliveryLayout(
        runId,
        artifactId,
        policy,
        { showErrorToast: false },
      );
      if (request !== sequence.current) return;
      if (
        response.code ||
        !response.data ||
        response.data.inputArtifactId !== artifactId ||
        (expectedFingerprint != null && response.data.inputFingerprint !== expectedFingerprint) ||
        (onReviewed && !response.data.projectionFingerprint) ||
        !deliveryContentLayoutsEqual(response.data.policy, policy)
      )
        throw new Error("DELIVERY_PREVIEW_UNAVAILABLE");
      setPreview(response.data);
      if (response.data.state === "available")
        onReviewed?.({
          projectionFingerprint: response.data.projectionFingerprint,
          changed: response.data.changed,
        });
    } catch {
      if (request === sequence.current) setError(true);
    } finally {
      if (request === sequence.current) setBusy(false);
    }
  };

  return (
    <section
      className="flex min-h-0 flex-1 flex-col gap-3 overflow-auto"
      aria-label={t("artifact.deliveryLayout.preview")}
    >
      <p className="shrink-0 text-xs text-default-500">
        {t("artifact.deliveryLayout.previewHint")}
      </p>
      {!callerPolicy && (
        <DeliveryContentLayoutFields
          value={policy}
          showExample={false}
          onChange={(value) => {
            sequence.current++;
            setPolicy(value);
            setPreview(undefined);
            setBusy(false);
            setError(false);
            setStale(true);
          }}
        />
      )}
      <div className="flex shrink-0 flex-wrap items-center gap-2">
        <Button
          size="sm"
          variant="flat"
          color="primary"
          isDisabled={busy || disabled}
          isLoading={busy}
          onPress={() => void load()}
        >
          {t("artifact.deliveryLayout.previewAction")}
        </Button>
        {stale && (
          <span className="text-xs text-default-500">
            {t("artifact.deliveryLayout.previewStale")}
          </span>
        )}
      </div>
      {busy && (
        <div className="flex justify-center py-6">
          <Spinner aria-label={t("artifact.deliveryLayout.previewLoading")} />
        </div>
      )}
      {error && (
        <p role="alert" className="text-sm text-warning-600">
          {t("artifact.deliveryLayout.previewFailed")}
        </p>
      )}
      {preview && (
        <div className="space-y-3" aria-live="polite">
          <div className="flex flex-wrap items-center gap-2 text-sm">
            <span>
              {preview.changed
                ? t("artifact.deliveryLayout.previewChanged", {
                    changed: preview.changedFileCount,
                    total: preview.fileCount,
                  })
                : t("artifact.deliveryLayout.previewUnchanged")}
            </span>
            <Chip size="sm" variant="flat">
              {t(`workflow.metadata.artifactState.${preview.state}`, {
                defaultValue: preview.state,
              })}
            </Chip>
          </div>
          {preview.truncated && (
            <p className="text-xs text-default-500">
              {t("artifact.deliveryLayout.previewTruncated")}
            </p>
          )}
          <PathMappings mappings={preview.fileMappings} />
          {!!preview.directoryMappings.length && (
            <div className="space-y-1">
              <h4 className="text-xs font-medium">
                {t("artifact.deliveryLayout.previewDirectories")}
              </h4>
              <PathMappings mappings={preview.directoryMappings} />
            </div>
          )}
          {!!preview.removedDirectories.length && (
            <div className="space-y-1 text-xs">
              <h4 className="font-medium">{t("artifact.deliveryLayout.previewRemoved")}</h4>
              <ul className="list-inside list-disc break-all">
                {preview.removedDirectories.map((path) => (
                  <li key={path}>{path}</li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}
    </section>
  );
}

function PathMappings({ mappings }: { mappings: Preview["fileMappings"] }) {
  const { t } = useTranslation();
  if (!mappings.length) return null;
  return (
    <table className="w-full table-fixed border-collapse text-left text-xs">
      <thead>
        <tr className="border-b border-default-200 text-default-500">
          <th className="p-2 font-normal">{t("artifact.deliveryLayout.before")}</th>
          <th className="p-2 font-normal">{t("artifact.deliveryLayout.after")}</th>
        </tr>
      </thead>
      <tbody>
        {mappings.map((item) => (
          <tr key={item.sourceRelativePath} className="border-b border-default-100">
            <td className="select-text break-all p-2 align-top">{item.sourceRelativePath}</td>
            <td className="select-text break-all p-2 align-top">
              {item.targetRelativePath || t("artifact.deliveryLayout.previewRoot")}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}
