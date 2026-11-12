import type { TFunction } from "i18next";
import { formatSourceAmount, sourceCurrencyLabel } from "./components/LockedContentDetails";

export interface PurchaseCurrencyTotal {
  currencyCode?: string | null;
  currencyName?: string | null;
  total: number;
  totalText?: string;
}
export interface PurchaseMoney {
  currencyCode?: string | null;
  currencyName?: string | null;
  knownTotalCost: number;
  knownTotalCostText?: string;
  currencyTotals?: PurchaseCurrencyTotal[];
}
export function purchaseCurrencyTotals(value: PurchaseMoney): PurchaseCurrencyTotal[] {
  return value.currencyTotals?.length
    ? value.currencyTotals
    : [
        {
          currencyCode: value.currencyCode,
          currencyName: value.currencyName,
          total: value.knownTotalCost,
          totalText: value.knownTotalCostText,
        },
      ];
}
export function hasKnownPurchaseCurrency(value: PurchaseMoney): boolean {
  return purchaseCurrencyTotals(value).every(
    (total) =>
      !!(total.currencyName?.trim() || total.currencyCode?.trim()) &&
      Number.isFinite(total.total) &&
      total.total >= 0,
  );
}
/** Currency totals are server quotes, never sums of prices printed on individual page blocks. */
export function purchaseTotalLabel(t: TFunction, value: PurchaseMoney): string {
  return purchaseCurrencyTotals(value)
    .map(
      (total) =>
        `${formatSourceAmount(total.totalText, total.total)} ${sourceCurrencyLabel(total.currencyName, total.currencyCode, undefined, t("postParser.lockedContent.currencyUnknown"))}`,
    )
    .join(" · ");
}
