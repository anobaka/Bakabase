import { useTranslation } from "react-i18next";

import { Checkbox, Select } from "@/components/bakaui";
import { ExHentaiDownloadMode } from "@/sdk/constants";
import {
  resolveExHentaiDownloadMode,
  type ExHentaiDownloadOptions,
  type ExHentaiDownloadPatch,
} from "@/pages/downloader/exHentaiDownloadMode";

interface Props {
  options?: ExHentaiDownloadOptions;
  onChange: (patch: ExHentaiDownloadPatch) => void;
}

export default function ExHentaiDownloadModeField({ options, onChange }: Props) {
  const { t } = useTranslation();
  const mode = resolveExHentaiDownloadMode(options);
  const btRoute = mode !== ExHentaiDownloadMode.Direct;
  const route = btRoute ? ExHentaiDownloadMode.Torrent : ExHentaiDownloadMode.Direct;
  const createBtTask = mode === ExHentaiDownloadMode.Torrent;
  const downloadTorrent = mode === ExHentaiDownloadMode.TorrentFile || !!options?.saveTorrentFile;
  const changeBtSelection = (downloadTorrent: boolean, createTask: boolean) => {
    if (!downloadTorrent && !createTask) return;
    onChange({
      downloadMode: createTask ? ExHentaiDownloadMode.Torrent : ExHentaiDownloadMode.TorrentFile,
      saveTorrentFile: createTask && downloadTorrent,
      ...(createTask && !createBtTask
        ? { fallbackToDirect: options?.fallbackToDirect ?? false }
        : {}),
    });
  };

  return (
    <div className="space-y-3 rounded-xl border border-default-200 p-3">
      <Select
        disallowEmptySelection
        dataSource={[
          { value: String(ExHentaiDownloadMode.Direct), label: t("downloader.mode.direct") },
          { value: String(ExHentaiDownloadMode.Torrent), label: t("downloader.mode.bt") },
        ]}
        label={t("downloader.mode.label")}
        selectedKeys={[String(route)]}
        size="sm"
        onSelectionChange={(keys) => {
          const key = Array.from(keys)[0];
          if (key == null) return;
          const downloadMode = Number(key) as ExHentaiDownloadMode;
          if (downloadMode === route) return;
          onChange({
            downloadMode,
            ...(downloadMode === ExHentaiDownloadMode.Torrent
              ? {
                  saveTorrentFile: options?.saveTorrentFile ?? false,
                  fallbackToDirect: options?.fallbackToDirect ?? false,
                }
              : {}),
          });
        }}
      />
      <p className="text-xs leading-relaxed text-default-500">
        {t(
          mode === ExHentaiDownloadMode.Torrent
            ? "downloader.mode.btDescription"
            : mode === ExHentaiDownloadMode.TorrentFile
              ? "downloader.mode.torrentFileDescription"
              : "downloader.mode.directDescription",
        )}
      </p>
      {btRoute && (
        <div className="space-y-2">
          <div className="flex flex-wrap gap-3">
            <Checkbox
              isSelected={downloadTorrent}
              isDisabled={downloadTorrent && !createBtTask}
              size="sm"
              onValueChange={(selected) => changeBtSelection(selected, createBtTask)}
            >
              {t("downloader.mode.downloadTorrent")}
            </Checkbox>
            <Checkbox
              isSelected={createBtTask}
              isDisabled={createBtTask && !downloadTorrent}
              size="sm"
              onValueChange={(selected) => changeBtSelection(downloadTorrent, selected)}
            >
              {t("downloader.mode.createBtTask")}
            </Checkbox>
          </div>
          <p className="text-xs text-default-500">{t("downloader.mode.btSelectionHint")}</p>
        </div>
      )}
      {createBtTask && (
        <div className="space-y-1">
          <Checkbox
            isSelected={options?.fallbackToDirect ?? true}
            size="sm"
            onValueChange={(fallbackToDirect) => onChange({ fallbackToDirect })}
          >
            {t("downloader.mode.fallbackToDirect")}
          </Checkbox>
          <p className="text-xs text-warning-600">{t("downloader.mode.fallbackDescription")}</p>
        </div>
      )}
    </div>
  );
}
