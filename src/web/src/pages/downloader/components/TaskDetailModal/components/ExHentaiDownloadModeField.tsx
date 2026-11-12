import { useRef } from "react";
import { useTranslation } from "react-i18next";

import { Button, Checkbox } from "@/components/bakaui";
import { ExHentaiDownloadMode } from "@/sdk/constants";
import {
  exHentaiDownloadPolicyPatch,
  exHentaiStopsWithoutTorrent,
  resolveExHentaiPolicyMode,
  type ExHentaiDownloadOptions,
  type ExHentaiDownloadPatch,
} from "@/pages/downloader/exHentaiDownloadMode";

interface Props {
  appearance?: "card" | "plain";
  options?: ExHentaiDownloadOptions;
  isExistingTask?: boolean;
  onChange: (patch: ExHentaiDownloadPatch) => void;
}

export default function ExHentaiDownloadModeField({
  appearance = "card",
  options,
  isExistingTask = false,
  onChange,
}: Props) {
  const { t } = useTranslation();
  const mode = resolveExHentaiPolicyMode(options, isExistingTask);
  const preferTorrent = mode !== ExHentaiDownloadMode.Direct;
  const savedBtSelection = useRef({ save: !!options?.saveTorrentFile, download: true });
  if (preferTorrent) {
    savedBtSelection.current = {
      save: mode === ExHentaiDownloadMode.TorrentFile || !!options?.saveTorrentFile,
      download: mode === ExHentaiDownloadMode.Torrent,
    };
  }
  const { save, download } = savedBtSelection.current;
  const stopsWithoutTorrent = exHentaiStopsWithoutTorrent(options, isExistingTask);
  const changePolicy = (prefer: boolean, saveFile = save, downloadContents = download) => {
    if (prefer && !saveFile && !downloadContents) return;
    onChange(exHentaiDownloadPolicyPatch(prefer, saveFile, downloadContents));
  };
  const checkboxClass = appearance === "plain" ? "m-0 px-0" : undefined;

  return (
    <div
      className={
        appearance === "plain" ? "space-y-3" : "space-y-3 rounded-xl border border-default-200 p-3"
      }
    >
      <div className="space-y-1">
        <Checkbox
          className={checkboxClass}
          isSelected={preferTorrent}
          size="sm"
          onValueChange={(value) => changePolicy(value)}
        >
          {t("downloader.policy.preferBt")}
        </Checkbox>
        <p className="text-xs leading-relaxed text-default-500">
          {t("downloader.policy.costHint")}
        </p>
      </div>
      {preferTorrent && (
        <div className="space-y-2 rounded-lg bg-default-50 p-3">
          <div className="flex flex-wrap gap-x-5 gap-y-2">
            <Checkbox
              isSelected={save}
              isDisabled={save && !download}
              size="sm"
              onValueChange={(value) => changePolicy(true, value, download)}
            >
              {t("downloader.policy.saveTorrent")}
            </Checkbox>
            <Checkbox
              isSelected={download}
              isDisabled={download && !save}
              size="sm"
              onValueChange={(value) => changePolicy(true, save, value)}
            >
              {t("downloader.policy.downloadBt")}
            </Checkbox>
          </div>
          <p className="text-xs text-default-500">
            {t(download ? "downloader.policy.btActionsHint" : "downloader.policy.torrentFileHint")}
          </p>
        </div>
      )}
      {stopsWithoutTorrent ? (
        <div
          className="flex flex-wrap items-center justify-between gap-2 rounded-lg bg-warning-50 p-3"
          role="note"
        >
          <p className="min-w-0 flex-1 text-xs text-warning-700">
            {t("downloader.policy.legacyNoFallback")}
          </p>
          <Button
            size="sm"
            variant="flat"
            color="warning"
            onPress={() => changePolicy(preferTorrent)}
          >
            {t("downloader.policy.adoptFallback")}
          </Button>
        </div>
      ) : (
        <p className="text-xs leading-relaxed text-default-500">
          {t(preferTorrent ? "downloader.policy.fallbackHint" : "downloader.policy.directHint")}
        </p>
      )}
    </div>
  );
}
