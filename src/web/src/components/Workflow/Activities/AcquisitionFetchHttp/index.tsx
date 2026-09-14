import { downloadTimeoutActivityUI } from "../AcquisitionDownloadConfig";

export const AcquisitionFetchHttpUI = downloadTimeoutActivityUI(
  "acquisition.fetchHttp",
  "workflow.acquisition.step.fetchHttp",
);
