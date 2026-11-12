import type { BakabaseInsideWorldBusinessComponentsPostParserModelsDomainPostParserTaskPurchasePreview as ApiPurchasePreview } from "@/sdk/Api";
import type { PurchaseMoney } from "../purchaseCurrency";
import type { TaskFilterScope } from "./TaskFilterScopeSummary";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineReload, AiOutlineUnlock } from "react-icons/ai";
import { Button, Spinner } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { sourceCurrencyLabel } from "./LockedContentDetails";
import {
  hasKnownPurchaseCurrency,
  purchaseCurrencyTotals,
  purchaseTotalLabel,
} from "../purchaseCurrency";
import { attentionReasonLabel, purchaseSkipReasonLabel } from "../attentionReasons";

export type PurchasePreview = ApiPurchasePreview & PurchaseMoney;
export interface PurchaseSubmissionSummary {
  queuedCount: number;
  skippedCount: number;
  failedCount: number;
}

export function purchaseScopeKey(scope: TaskFilterScope): string {
  return JSON.stringify([
    scope.keyword?.trim() ?? "",
    scope.status ?? "all",
    scope.reasonCode ?? "",
  ]);
}

function validPreview(preview: PurchasePreview, scope: TaskFilterScope): boolean {
  return (
    !!preview.scope &&
    purchaseScopeKey(preview.scope as TaskFilterScope) === purchaseScopeKey(scope) &&
    Number.isSafeInteger(preview.matchedCount) &&
    preview.matchedCount >= 0 &&
    Number.isSafeInteger(preview.actionableCount) &&
    preview.actionableCount >= 0 &&
    preview.actionableCount <= preview.matchedCount &&
    Array.isArray(preview.tasks) &&
    preview.tasks.length === preview.actionableCount &&
    new Set(preview.tasks.map((task) => task.taskId)).size === preview.tasks.length &&
    preview.tasks.every(
      (task) =>
        Array.isArray(task.lockUrls) &&
        task.lockUrls.length > 0 &&
        task.lockUrls.every((url) => typeof url === "string" && !!url.trim()),
    ) &&
    Number.isFinite(preview.knownTotalCost) &&
    preview.knownTotalCost >= 0 &&
    Number.isFinite(preview.autoBuyThreshold) &&
    Number.isFinite(preview.minimumRemainingCoins) &&
    !!preview.skipReasonCounts &&
    typeof preview.skipReasonCounts === "object"
  );
}

/** A read-only quote snapshot for one explicitly selected attention category. */
export default function PurchaseScopePanel({
  scope,
  refreshVersion,
  queryReady = true,
  disabled,
  submission,
  onReview,
}: {
  scope: TaskFilterScope;
  refreshVersion: string;
  queryReady?: boolean;
  disabled?: boolean;
  submission?: PurchaseSubmissionSummary;
  onReview: (preview: PurchasePreview) => void;
}) {
  const { t } = useTranslation();
  const query = purchaseScopeKey(scope);
  const enabled = scope.status === "attention" && !!scope.reasonCode;
  const [reload, setReload] = useState(0);
  const requestKey = JSON.stringify([query, refreshVersion, reload]);
  const latestRequestKey = useRef(requestKey);
  latestRequestKey.current = requestKey;
  const [result, setResult] = useState<{
    query: string;
    requestKey: string;
    preview: PurchasePreview;
  }>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  useEffect(() => {
    if (!enabled) return;
    setLoading(true);
    setError(false);
    if (!queryReady) return;
    let cancelled = false;
    const controller = new AbortController();
    const [keyword, status, reasonCode] = JSON.parse(query) as string[];
    const requestScope = { keyword: keyword || undefined, status, reasonCode };
    void (async () => {
      try {
        const response = await BApi.postParser.previewPostParserTaskPurchases(requestScope, {
          signal: controller.signal,
          sensitiveResponse: true,
          showErrorToast: false,
        });
        if (cancelled || latestRequestKey.current !== requestKey) return;
        if (
          response.code ||
          !response.data ||
          !validPreview(response.data, requestScope as TaskFilterScope)
        )
          throw new Error("PURCHASE_PREVIEW_UNAVAILABLE");
        setResult({ query, requestKey, preview: response.data });
      } catch {
        if (!cancelled && latestRequestKey.current === requestKey) setError(true);
      } finally {
        if (!cancelled && latestRequestKey.current === requestKey) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
      controller.abort();
    };
  }, [enabled, query, queryReady, requestKey]);

  if (!enabled) return null;
  const preview = result?.query === query ? result.preview : undefined;
  const fresh = queryReady && result?.requestKey === requestKey && !loading && !error;
  const itemCount = preview?.tasks.reduce((count, task) => count + task.lockUrls.length, 0);
  const currencyTotals = preview ? purchaseCurrencyTotals(preview) : [];
  const currency = sourceCurrencyLabel(
    currencyTotals[0]?.currencyName,
    currencyTotals[0]?.currencyCode,
    undefined,
    t("postParser.lockedContent.currencyUnknown"),
  );
  return (
    <section
      aria-label={t("postParser.purchaseScope.label")}
      className="w-full space-y-3 rounded-lg border border-warning/25 bg-warning/5 p-3 text-sm"
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <p className="font-medium">
          {t("postParser.purchaseScope.title", {
            reason: attentionReasonLabel(t, scope.reasonCode!),
          })}
        </p>
        <Button
          size="sm"
          variant="light"
          startContent={<AiOutlineReload aria-hidden />}
          isDisabled={disabled || loading}
          onPress={() => setReload((value) => value + 1)}
        >
          {t("postParser.purchaseScope.refresh")}
        </Button>
      </div>
      <div className="h-5 min-w-0">
        {(loading || (!fresh && !error)) && (
          <div
            role="status"
            className="flex h-5 min-w-0 items-center gap-2 text-xs text-default-500"
          >
            <Spinner size="sm" classNames={{ wrapper: "h-3 w-3 shrink-0" }} />
            <span
              className="truncate"
              title={t(
                preview
                  ? "postParser.purchaseScope.refreshing"
                  : "postParser.purchaseScope.loading",
              )}
            >
              {t(
                preview
                  ? "postParser.purchaseScope.refreshing"
                  : "postParser.purchaseScope.loading",
              )}
            </span>
          </div>
        )}
      </div>
      {preview && (
        <dl
          aria-label={t("postParser.purchaseScope.summary")}
          className={`grid grid-cols-2 gap-3 sm:grid-cols-4 ${fresh ? "" : "opacity-60"}`}
        >
          {[
            ["matched", preview.matchedCount],
            ["actionable", preview.actionableCount],
            ["items", itemCount],
            ["cost", purchaseTotalLabel(t, preview)],
          ].map(([label, value]) => (
            <div key={String(label)}>
              <dt className="text-xs text-default-500">{t(`postParser.purchaseScope.${label}`)}</dt>
              <dd className="mt-1 font-medium tabular-nums">{value}</dd>
            </div>
          ))}
        </dl>
      )}
      {preview && (
        <p className="text-xs text-default-500">
          {currencyTotals.length > 1
            ? t("postParser.purchaseBatch.multipleCurrencyLimits")
            : t("postParser.purchaseBatch.limits", {
                limit: preview.autoBuyThreshold,
                reserve: preview.minimumRemainingCoins,
                currency,
              })}
        </p>
      )}
      {error && (
        <div role="alert" className="text-danger">
          {t("postParser.purchaseScope.failed")}
        </div>
      )}
      {preview && Object.entries(preview.skipReasonCounts).some(([, count]) => count > 0) && (
        <ul className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-default-500">
          {Object.entries(preview.skipReasonCounts)
            .filter(([, count]) => count > 0)
            .map(([code, count]) => (
              <li key={code}>
                {purchaseSkipReasonLabel(t, code)}：{count}
              </li>
            ))}
        </ul>
      )}
      <div className="flex flex-wrap items-center gap-3">
        <Button
          color="warning"
          size="sm"
          className="h-auto min-w-0 max-w-full whitespace-normal py-2"
          startContent={<AiOutlineUnlock aria-hidden />}
          isDisabled={
            disabled || !fresh || !preview?.actionableCount || !hasKnownPurchaseCurrency(preview)
          }
          onPress={() => {
            if (!disabled && fresh && preview?.actionableCount && hasKnownPurchaseCurrency(preview))
              onReview(preview);
          }}
        >
          {preview
            ? t("postParser.purchaseScope.review", {
                items: itemCount,
                total: purchaseTotalLabel(t, preview),
              })
            : t("postParser.purchaseBatch.action")}
        </Button>
        <p className="text-xs text-default-500">{t("postParser.purchaseScope.hint")}</p>
      </div>
      {preview && preview.actionableCount > 0 && !hasKnownPurchaseCurrency(preview) && (
        <p role="alert" className="text-xs text-warning-700">
          {t("postParser.purchaseBatch.currencyUnknown")}
        </p>
      )}
      {preview && !preview.actionableCount && (
        <p role="status" className="text-xs text-warning-700">
          {t("postParser.purchaseBatch.none")}
        </p>
      )}
      {submission && (
        <p role="status" className="text-xs leading-relaxed text-primary">
          {t("postParser.purchaseBatch.result", {
            queued: submission.queuedCount,
            skipped: submission.skippedCount,
            failed: submission.failedCount,
          })}
        </p>
      )}
    </section>
  );
}
