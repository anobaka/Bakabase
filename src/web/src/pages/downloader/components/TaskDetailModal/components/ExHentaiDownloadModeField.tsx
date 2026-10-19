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

  return (
    <div className="space-y-3 rounded-xl border border-default-200 p-3">
      <Select
        disallowEmptySelection
        dataSource={[
          { value: String(ExHentaiDownloadMode.Direct), label: t("downloader.mode.direct") },
          { value: String(ExHentaiDownloadMode.Torrent), label: t("downloader.mode.bt") },
          {
            value: String(ExHentaiDownloadMode.TorrentFile),
            label: t("downloader.mode.torrentFile"),
          },
        ]}
        label={t("downloader.mode.label")}
        selectedKeys={[String(mode)]}
        size="sm"
        onSelectionChange={(keys) => {
          const key = Array.from(keys)[0];
          if (key != null) onChange({ downloadMode: Number(key) as ExHentaiDownloadMode });
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
      {mode === ExHentaiDownloadMode.Torrent && (
        <Checkbox
          isSelected={options?.saveTorrentFile ?? false}
          size="sm"
          onValueChange={(saveTorrentFile) => onChange({ saveTorrentFile })}
        >
          {t("downloader.mode.saveTorrentFile")}
        </Checkbox>
      )}
      {mode !== ExHentaiDownloadMode.Direct && (
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
