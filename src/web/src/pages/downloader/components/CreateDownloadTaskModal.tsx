import { useEffect, useId, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import { FiFile, FiGlobe } from "react-icons/fi";
import { TbMagnet } from "react-icons/tb";
import { Button, Modal, Spinner } from "@/components/bakaui";
import type { DestroyableProps } from "@/components/bakaui/types";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import AcquireDownloadForm from "@/components/Acquire/AcquireDownloadForm";
import BApi from "@/sdk/BApi";
import { CloudDownloadSources, ThirdPartyId } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";
import type { BakabaseInsideWorldBusinessComponentsDownloaderAbstractionsModelsDownloaderDefinition as DownloaderDefinition } from "@/sdk/Api";
import { GeneralDownloadForm, type GeneralDownloadKind } from "./GeneralDownloadForm";
import { SiteDownloadForm } from "./TaskDetailModal";
import type { DownloadFormState } from "./downloadForm";
import { DownloadMethodStatusChip } from "@/components/CloudDownload/DownloadMethodAvailability";

const cloudProviders = new Map(
  CloudDownloadSources.map((source) => [source.thirdPartyId, source.providerId]),
);
const generalMethods = [
  { id: "http", label: "downloadCenter.provider.http", Icon: FiGlobe },
  { id: "bt", label: "downloadCenter.provider.bt", Icon: TbMagnet },
  { id: "file", label: "bt.add.file", Icon: FiFile },
] as const;

export default function CreateDownloadTaskModal({
  onDestroyed,
  onCreated,
}: DestroyableProps & { onCreated: () => void }) {
  const { t } = useTranslation();
  const formId = useId();
  const sourceLabelId = useId();
  const [visible, setVisible] = useState(true);
  const [method, setMethod] = useState("http");
  const [state, setState] = useState<DownloadFormState>({ canSubmit: false, busy: false });
  const [definitions, setDefinitions] = useState<DownloaderDefinition[]>();
  const [loadError, setLoadError] = useState<string>();
  const [reload, setReload] = useState(0);
  useEffect(() => {
    let active = true;
    setLoadError(undefined);
    void BApi.downloadTask
      .getAllDownloaderDefinitions()
      .then((response) => {
        if (response.code) throw new Error(response.message || t("downloadCenter.error.action"));
        if (active) setDefinitions(response.data ?? []);
      })
      .catch((error) => {
        if (active)
          setLoadError(error instanceof Error ? error.message : t("downloadCenter.error.action"));
      });
    return () => {
      active = false;
    };
  }, [reload]);
  const sites = useMemo(() => {
    const ids = new Set<ThirdPartyId>(cloudProviders.keys());
    definitions?.forEach((definition) => ids.add(definition.thirdPartyId));
    return [...ids].sort((a, b) => a - b);
  }, [definitions]);
  const thirdPartyId = method.startsWith("site:")
    ? (Number(method.slice(5)) as ThirdPartyId)
    : undefined;
  const cloudProvider = thirdPartyId == null ? undefined : cloudProviders.get(thirdPartyId);
  const select = (value: string) => {
    if (state.busy || value === method) return;
    setState({ canSubmit: false, busy: false });
    setMethod(value);
  };
  const created = () => {
    onCreated();
    setVisible(false);
  };

  return (
    <Modal
      visible={visible}
      size="4xl"
      title={t("downloader.modal.creatingTask")}
      onDestroyed={onDestroyed}
      onClose={() => setVisible(false)}
      isDismissable={!state.busy}
      isKeyboardDismissDisabled={state.busy}
      hideCloseButton={state.busy}
      footer={
        <>
          <Button isDisabled={state.busy} variant="light" onPress={() => setVisible(false)}>
            {t("common.action.cancel")}
          </Button>
          <Button
            color="primary"
            type="submit"
            form={formId}
            isLoading={state.busy}
            isDisabled={!state.canSubmit || state.busy}
          >
            {t("downloader.action.createTask")}
          </Button>
        </>
      }
    >
      <h3 id={sourceLabelId} className="text-sm font-medium">
        {t("downloadCenter.add.source")}
      </h3>
      <div role="group" aria-labelledby={sourceLabelId} className="flex flex-wrap gap-2">
        {generalMethods.map(({ id, label, Icon }) => (
          <Button
            key={id}
            aria-pressed={method === id}
            isDisabled={state.busy}
            color={method === id ? "primary" : "default"}
            variant="flat"
            size="sm"
            className="h-8 min-w-0 shrink-0 gap-2 px-2"
            onPress={() => select(id)}
          >
            <span
              aria-hidden="true"
              className="flex size-4 shrink-0 items-center justify-center"
              data-download-method-icon={id}
            >
              <Icon size={16} />
            </span>
            {t(label)}
          </Button>
        ))}
        {sites.map((id) => {
          const key = `site:${id}`;
          const name = ThirdPartyId[id];
          const fallback = definitions?.find((definition) => definition.thirdPartyId === id)?.name;
          return (
            <Button
              key={key}
              aria-pressed={method === key}
              isDisabled={state.busy}
              color={method === key ? "primary" : "default"}
              variant="flat"
              size="sm"
              className="h-8 min-w-0 shrink-0 gap-2 px-2"
              onPress={() => select(key)}
            >
              <span
                aria-hidden="true"
                className="flex size-4 shrink-0 items-center justify-center [&>*]:!size-4"
                data-download-method-icon={key}
              >
                <ThirdPartyIcon thirdPartyId={id} />
              </span>
              {name ? t(getEnumKey("ThirdPartyId", name)) : fallback}
              <DownloadMethodStatusChip providerId={cloudProviders.get(id)} />
            </Button>
          );
        })}
        {!definitions && !loadError && <Spinner size="sm" />}
      </div>
      {loadError && (
        <div role="alert" className="text-sm text-danger">
          {loadError}
          <Button size="sm" variant="light" onPress={() => setReload((value) => value + 1)}>
            {t("common.action.retry")}
          </Button>
        </div>
      )}
      {cloudProvider ? (
        <AcquireDownloadForm
          key={method}
          formId={formId}
          providerId={cloudProvider}
          onStateChange={setState}
          onCreated={created}
        />
      ) : thirdPartyId != null ? (
        <SiteDownloadForm
          key={method}
          formId={formId}
          thirdPartyId={thirdPartyId}
          definitions={definitions}
          onStateChange={setState}
          onCreated={created}
        />
      ) : (
        <GeneralDownloadForm
          key={method}
          formId={formId}
          kind={method as GeneralDownloadKind}
          onStateChange={setState}
          onCreated={created}
        />
      )}
    </Modal>
  );
}
