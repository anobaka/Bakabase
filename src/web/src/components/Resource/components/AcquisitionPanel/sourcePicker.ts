import { AcquisitionLeadKind } from "@/sdk/constants";

export type SourceKind =
  | AcquisitionLeadKind.DirectUrl
  | AcquisitionLeadKind.SharedPage
  | AcquisitionLeadKind.SharedDocument
  | AcquisitionLeadKind.Magnet;

export const sourceMethods = [
  { id: "directUrl", kind: AcquisitionLeadKind.DirectUrl },
  { id: "sharedPage", kind: AcquisitionLeadKind.SharedPage },
  { id: "sharedDocument", kind: AcquisitionLeadKind.SharedDocument },
  { id: "magnet", kind: AcquisitionLeadKind.Magnet },
] as const;

export const MAX_SOURCE_LENGTH = 2048;

const parseUrl = (value: string) => {
  try {
    return new URL(value);
  } catch {
    return undefined;
  }
};

const isHttpUrl = (value: string, url: URL | undefined) =>
  /^https?:\/\//i.test(value) && !/\s/.test(value) && !!url?.hostname;

/** Recognizable sharing pages are not file URLs. Other URLs still need runtime verification. */
const isKnownSharingPage = (url: URL) => {
  const host = url.hostname.toLowerCase().replace(/^www\./, "");

  return (
    /(^|\.)(soul-plus|south-plus|north-plus)\.(net|org)$/.test(host) ||
    (["pan.baidu.com", "yun.baidu.com", "pan.quark.cn", "alipan.com", "aliyundrive.com"].includes(
      host,
    ) &&
      /^\/(s\/|share\/)/i.test(url.pathname)) ||
    (["mega.nz", "mega.co.nz"].includes(host) &&
      (/^\/(file|folder)\//i.test(url.pathname) || /^#(?:!|F!)/.test(url.hash))) ||
    host === "1drv.ms" ||
    (host === "drive.google.com" && /^\/(file|drive)\//i.test(url.pathname))
  );
};

export const validateAcquisitionSource = (kind: SourceKind | undefined, raw: string) => {
  const value = raw.trim();

  if (!sourceMethods.some((method) => method.kind === kind)) return "chooseMethod";
  if (!value) return "required";
  if (value.length > MAX_SOURCE_LENGTH) return "tooLong";

  const url = parseUrl(value);

  switch (kind) {
    case AcquisitionLeadKind.DirectUrl:
    case AcquisitionLeadKind.SharedPage:
      if (!isHttpUrl(value, url)) return "httpUrl";
      if (kind === AcquisitionLeadKind.DirectUrl && isKnownSharingPage(url!)) return "sharingPage";

      return undefined;
    case AcquisitionLeadKind.SharedDocument:
      if (value.length < 8) return "textTooShort";
      if (url) return "textOnlyUrl";

      return undefined;
    case AcquisitionLeadKind.Magnet:
      if (!/^magnet:\?/i.test(value) || !url || /\s/.test(value)) return "magnet";
      if (
        !url.searchParams
          .getAll("xt")
          .some(
            (xt) =>
              /^urn:btih:(?:[a-f0-9]{40}|[a-z2-7]{32})$/i.test(xt) ||
              /^urn:btmh:1220[a-f0-9]{64}$/i.test(xt),
          )
      ) {
        return "magnet";
      }

      return undefined;
  }
};
