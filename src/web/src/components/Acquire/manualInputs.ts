import type { DownloadCandidate } from "./models";
import { CloudDownloadSources } from "@/sdk/constants";

/** Explicit manual choices, separate from automatic classification of source-module candidates. */
export const manualInputMethods: {
  id: string;
  labelKey: string;
  kind: string;
  providerId?: string;
  methodId?: string;
}[] = [
  { id: "auto", labelKey: "acquire.kind.auto", kind: "auto" },
  { id: "http", labelKey: "acquire.kind.http", kind: "http" },
  { id: "torrent", labelKey: "acquire.kind.torrent", kind: "torrent" },
  { id: "baiduRemote", labelKey: "acquire.kind.remote", kind: "remote", providerId: "baidu" },
  ...CloudDownloadSources.filter(
    (source) => source.supportsRemote && source.providerId !== "baidu",
  ).map((source) => ({
    id: `${source.providerId}Remote`,
    labelKey: `acquire.kind.remote.${source.providerId}`,
    kind: "remote",
    providerId: source.providerId,
    methodId: source.methods.find((method) => method.sourceKinds.includes("remote"))?.id,
  })),
];
export function manualCandidate(
  inputId: string,
  values: Omit<DownloadCandidate, "kind" | "providerId">,
): DownloadCandidate {
  const choice =
    manualInputMethods.find((method) => method.id === inputId) ?? manualInputMethods[0];
  return {
    ...values,
    kind: choice.kind,
    providerId: choice.providerId,
    ...(choice.methodId ? { methodId: choice.methodId } : {}),
  };
}
