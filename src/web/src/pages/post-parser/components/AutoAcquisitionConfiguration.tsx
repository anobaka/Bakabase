import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Checkbox } from "@/components/bakaui";
import AcquisitionSettingsModal from "@/pages/acquisition/components/AcquisitionSettingsModal";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import DownloadDirectoryField from "@/components/ThirdPartyConfig/base/DownloadDirectoryField";
import BApi from "@/sdk/BApi";
import { useDownloaderGlobalOptionsStore, useThirdPartyOptionsStore } from "@/stores/options";

export default function AutoAcquisitionConfiguration() {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const savedEnabled = useThirdPartyOptionsStore(
    (state) => state.data.postParserAcquisition?.enabled ?? false,
  );
  const savedDirectory = useThirdPartyOptionsStore(
    (state) => state.data.postParserAcquisition?.outputDirectory ?? "",
  );
  const defaultDirectory = useDownloaderGlobalOptionsStore(
    (state) => state.data.defaultDownloadPath?.trim() ?? "",
  );
  const [enabled, setEnabled] = useState(savedEnabled);
  const [directory, setDirectory] = useState(savedDirectory);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string>();
  const [saved, setSaved] = useState(false);
  const savingRef = useRef(false);

  useEffect(() => {
    setEnabled(savedEnabled);
    setDirectory(savedDirectory);
  }, [savedEnabled, savedDirectory]);
  const effectiveDirectory = directory.trim() || defaultDirectory;
  const save = async () => {
    if (savingRef.current || (enabled && !effectiveDirectory)) return;
    savingRef.current = true;
    setSaving(true);
    setSaved(false);
    setError(undefined);
    const postParserAcquisition = { enabled, outputDirectory: directory.trim() };

    try {
      const response = await BApi.options.patchThirdPartyOptions({ postParserAcquisition });

      if (response.code) throw new Error(response.message || t("postParser.result.failed"));
      useThirdPartyOptionsStore.getState().update({ postParserAcquisition });
      setSaved(true);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : t("postParser.result.failed"));
    } finally {
      savingRef.current = false;
      setSaving(false);
    }
  };

  return (
    <section
      aria-label={t("postParser.config.acquisition.title")}
      className="space-y-3 border-t border-default-200 pt-4"
    >
      <h3 className="text-sm font-medium">{t("postParser.config.acquisition.title")}</h3>
      <Checkbox
        isDisabled={saving}
        isSelected={enabled}
        onValueChange={(value) => {
          setEnabled(value);
          setSaved(false);
        }}
      >
        {t("postParser.config.acquisition.enabled")}
      </Checkbox>
      <p className="text-sm leading-relaxed text-default-500">
        {t("postParser.config.acquisition.triggerHint")}
      </p>
      <DownloadDirectoryField
        clearLabel={t("postParser.config.acquisition.useDefault")}
        description={
          <div className="space-y-1">
            <p>{t("postParser.config.acquisition.directoryHint")}</p>
            {!directory.trim() && (
              <p className="break-all">
                {defaultDirectory
                  ? t("postParser.config.acquisition.inheritedDirectory", {
                      path: defaultDirectory,
                    })
                  : t("postParser.config.acquisition.noDefaultDirectory")}
              </p>
            )}
          </div>
        }
        isDisabled={saving}
        label={t("postParser.config.acquisition.directory")}
        placeholder={t("postParser.config.acquisition.chooseDirectory")}
        startPath={defaultDirectory || undefined}
        value={directory}
        onChange={(value) => {
          setDirectory(value);
          setSaved(false);
        }}
      />
      <p className="text-xs leading-relaxed text-default-500">
        {t("postParser.config.acquisition.selectionHint")}
      </p>
      <p className="text-xs leading-relaxed text-default-500">
        {t("postParser.config.acquisition.executionHint")}
      </p>
      {enabled && !effectiveDirectory && (
        <p className="text-sm text-warning-600" role="status">
          {t("postParser.config.acquisition.directoryRequired")}
        </p>
      )}
      <Button
        color="primary"
        isDisabled={saving || (enabled && !effectiveDirectory)}
        isLoading={saving}
        size="sm"
        onPress={save}
      >
        {t("postParser.config.acquisition.save")}
      </Button>
      {saved && (
        <p className="text-sm text-success" role="status">
          {t("postParser.config.acquisition.saved")}
        </p>
      )}
      {error && (
        <p className="text-sm text-danger" role="alert">
          {error}
        </p>
      )}
      <div className="border-t border-default-200 pt-4">
        <Button
          size="sm"
          variant="light"
          onPress={() => createPortal(AcquisitionSettingsModal, { initialTab: "sources" })}
        >
          {t("acquisition.settings.openSources")}
        </Button>
      </div>
    </section>
  );
}
