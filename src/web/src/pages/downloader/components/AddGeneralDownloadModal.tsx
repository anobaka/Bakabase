import type { DestroyableProps } from "@/components/bakaui/types";
import { useId, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Modal, Tab, Tabs } from "@/components/bakaui";
import { GeneralDownloadForm, type GeneralDownloadKind } from "./GeneralDownloadForm";
import type { DownloadFormHandle, DownloadFormState } from "./downloadForm";

export default function AddGeneralDownloadModal({
  onDestroyed,
  onCreated,
}: DestroyableProps & { onCreated: () => void }) {
  const { t } = useTranslation();
  const [kind, setKind] = useState<GeneralDownloadKind>("bt");
  const [state, setState] = useState<DownloadFormState>({ canSubmit: false, busy: false });
  const form = useRef<DownloadFormHandle>(null);
  const formId = useId();
  return (
    <Modal
      defaultVisible
      size="2xl"
      title={t("downloadCenter.add.general")}
      onDestroyed={onDestroyed}
      isDismissable={!state.busy}
      footer={{
        actions: ["ok", "cancel"],
        okProps: { isDisabled: !state.canSubmit || state.busy },
      }}
      onOk={() => form.current?.submit()}
    >
      <Tabs
        selectedKey={kind}
        isDisabled={state.busy}
        onSelectionChange={(key) => {
          setState({ canSubmit: false, busy: false });
          setKind(String(key) as GeneralDownloadKind);
        }}
      >
        <Tab key="bt" title={t("downloadCenter.provider.bt")} />
        <Tab key="http" title={t("downloadCenter.provider.http")} />
        <Tab key="file" title={t("bt.add.file")} />
      </Tabs>
      <GeneralDownloadForm
        key={kind}
        ref={form}
        formId={formId}
        kind={kind}
        onStateChange={setState}
        onCreated={onCreated}
      />
    </Modal>
  );
}
