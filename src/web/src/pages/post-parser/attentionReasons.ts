import type { TFunction } from "i18next";

/** Codes are classified by the server; the UI only supplies presentation. */
export function attentionReasonLabel(t: TFunction, code: string): string {
  const key = `postParser.attentionReason.${code}`;
  const label = t<string>(key);
  return label === key ? t<string>("postParser.attentionReason.other", { code }) : label;
}

export function purchaseSkipReasonLabel(t: TFunction, code: string): string {
  const key = `postParser.purchaseBatch.skip.${code}`;
  const label = t<string>(key);
  return label === key ? t<string>("postParser.purchaseBatch.skip.other", { code }) : label;
}
