import { useTranslation } from "react-i18next";

import { downloadTimeoutActivityUI } from "../AcquisitionDownloadConfig";

const base = downloadTimeoutActivityUI(
  "action.downloader.fetchTorrentResult",
  "workflow.activity.downloaderFetchTorrentResult.displayName",
);
const TimeoutForm = base.ConfigForm;

export const DownloaderFetchTorrentResultUI: typeof base = {
  ...base,
  ConfigForm: (props) => {
    const { t } = useTranslation();

    return (
      <div className="space-y-3">
        <p className="text-xs leading-relaxed text-default-500">
          {t("workflow.activity.downloaderFetchTorrentResult.description")}
        </p>
        <TimeoutForm {...props} />
      </div>
    );
  },
};
