import { useTranslation } from "react-i18next";

import { downloadTimeoutActivityUI } from "../AcquisitionDownloadConfig";

const base = downloadTimeoutActivityUI(
  "acquisition.fetchResultTorrent",
  "workflow.acquisition.step.fetchResultTorrent",
);
const TimeoutForm = base.ConfigForm;

export const AcquisitionFetchResultTorrentUI: typeof base = {
  ...base,
  ConfigForm: (props) => {
    const { t } = useTranslation();

    return (
      <div className="space-y-3">
        <p className="text-xs leading-relaxed text-default-500">
          {t("workflow.acquisition.fetchResultTorrent.description")}
        </p>
        <TimeoutForm {...props} />
      </div>
    );
  },
};
