import { createContext } from "react";
import type { BakabaseServiceComponentsCloudDownloadsCloudDownloadResumeTarget } from "@/sdk/Api";

/** A task owner can atomically apply its execution options with the node's unchanged answer. */
export const ResumeSubmissionContext = createContext<{
  runId: number;
  downloadTargets?: readonly BakabaseServiceComponentsCloudDownloadsCloudDownloadResumeTarget[];
  submit: (signalJson: string) => Promise<boolean>;
} | undefined>(undefined);
