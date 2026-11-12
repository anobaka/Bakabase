import { createContext } from "react";

/** A task owner can atomically apply its execution options with the node's unchanged answer. */
export const ResumeSubmissionContext = createContext<{
  runId: number;
  submit: (signalJson: string) => Promise<boolean>;
} | undefined>(undefined);
