import { useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineLink } from "react-icons/ai";
import { Button, Input, Modal } from "@/components/bakaui";

type Props = {
  disabled: boolean;
  onUse: (source: { url: string; accessCode: string }) => Promise<void>;
};

function canSubmit(value: string) {
  try {
    const url = new URL(value.trim());
    return value.trim().length <= 2048 && ["http:", "https:"].includes(url.protocol) && !url.username && !url.password;
  } catch {
    return false;
  }
}

export default function ManualSourceAction({ disabled, onUse }: Props) {
  const { t } = useTranslation();
  const [open, setOpen] = useState(false);
  const [url, setUrl] = useState("");
  const [accessCode, setAccessCode] = useState("");
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  const save = async () => {
    if (disabled || saving || !canSubmit(url)) return;
    setSaving(true);
    setError("");
    try {
      await onUse({ url: url.trim(), accessCode: accessCode.trim() });
      setOpen(false);
    } catch (failure) {
      const code = failure instanceof Error ? failure.message : "";
      if (code.startsWith("SOURCE_REPLACEMENT_DUPLICATE")) setError("acquisition.sourceImage.status.duplicate");
      else if (/PLAN_(VERSION_CONFLICT|ALREADY_STARTED)|SOURCE_REPLACEMENT_LOCAL_FILES/.test(code)) setError("acquisition.sourceImage.status.taskChanged");
      else if (/SOURCE_REPLACEMENT_(UNSUPPORTED|INVALID)/.test(code)) setError("acquisition.manualSource.unsupported");
      else setError("acquisition.sourceImage.status.saveFailed");
    } finally {
      setSaving(false);
    }
  };
  return (
    <>
      <Button size="sm" variant="flat" isDisabled={disabled} startContent={<AiOutlineLink aria-hidden />}
        onPress={() => { setUrl(""); setAccessCode(""); setError(""); setOpen(true); }}>
        {t("acquisition.manualSource.title")}
      </Button>
      {open && (
        <Modal visible title={t("acquisition.manualSource.title")} onClose={() => { if (!saving) setOpen(false); }}
          isDismissable={!saving} isKeyboardDismissDisabled={saving}
          footer={<>
            <Button variant="light" isDisabled={saving} onPress={() => setOpen(false)}>{t("common.action.close")}</Button>
            <Button color="primary" isDisabled={disabled || !canSubmit(url) || saving} isLoading={saving} onPress={() => void save()}>
              {t("acquisition.sourceImage.use")}
            </Button>
          </>}>
          <Input label={t("acquisition.manualSource.url")} type="url" autoComplete="off" maxLength={2048}
            description={t("acquisition.manualSource.hint")} value={url} onValueChange={setUrl} isDisabled={saving} />
          <Input label={t("acquisition.sourceCorrection.accessCode")} maxLength={512} value={accessCode}
            onValueChange={setAccessCode} isDisabled={saving} />
          {error && <p role="alert" className="text-sm text-danger">{t(error)}</p>}
        </Modal>
      )}
    </>
  );
}
