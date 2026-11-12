import type { DestroyableProps } from "@/components/bakaui/types";
import type { PurchaseCurrencyTotal } from "../purchaseCurrency";
import type { TaskFilterScope } from "./TaskFilterScopeSummary";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineUnlock } from "react-icons/ai";
import { Button, Modal } from "@/components/bakaui";
import TaskFilterScopeSummary from "./TaskFilterScopeSummary";
import {
  hasKnownPurchaseCurrency,
  purchaseCurrencyTotals,
  purchaseTotalLabel,
} from "../purchaseCurrency";
import { sourceCurrencyLabel } from "./LockedContentDetails";
import { purchaseSkipReasonLabel } from "../attentionReasons";

interface Props extends DestroyableProps {
  scope: TaskFilterScope;
  matchedCount: number;
  actionableCount: number;
  purchaseItemCount: number;
  knownTotalCost: number;
  knownTotalCostText?: string;
  currencyCode?: string | null;
  currencyName?: string | null;
  currencyTotals?: PurchaseCurrencyTotal[];
  skipReasonCounts: Record<string, number>;
  autoBuyThreshold: number;
  minimumRemainingCoins: number;
  onConfirm: () => Promise<void>;
  onClose?: () => void;
}

/** Approval is for the frozen, priced locks in the preview, not a future broader query. */
export default function PurchaseTasksConfirmationModal({
  scope,
  matchedCount,
  actionableCount,
  purchaseItemCount,
  knownTotalCost,
  knownTotalCostText,
  currencyCode,
  currencyName,
  currencyTotals,
  skipReasonCounts,
  autoBuyThreshold,
  minimumRemainingCoins,
  onConfirm,
  onClose,
  onDestroyed,
}: Props) {
  const { t } = useTranslation();
  const [visible, setVisible] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string>();
  const submittingRef = useRef(false);
  const cancelButton = useRef<HTMLButtonElement>(null);
  useEffect(() => {
    const frame = window.requestAnimationFrame(() => cancelButton.current?.focus());
    return () => window.cancelAnimationFrame(frame);
  }, []);
  const money = { knownTotalCost, knownTotalCostText, currencyCode, currencyName, currencyTotals };
  const totalLabel = purchaseTotalLabel(t, money);
  const currencies = purchaseCurrencyTotals(money);
  const currency = sourceCurrencyLabel(
    currencies[0]?.currencyName,
    currencies[0]?.currencyCode,
    undefined,
    t("postParser.lockedContent.currencyUnknown"),
  );
  const valid =
    hasKnownPurchaseCurrency(money) &&
    Number.isSafeInteger(actionableCount) &&
    actionableCount > 0 &&
    actionableCount <= matchedCount &&
    Number.isFinite(knownTotalCost) &&
    knownTotalCost >= 0;
  const close = () => {
    if (!submittingRef.current) {
      setVisible(false);
      onClose?.();
    }
  };
  const confirm = async () => {
    if (!valid || submittingRef.current) return;
    submittingRef.current = true;
    setSubmitting(true);
    setError(undefined);
    try {
      await onConfirm();
      submittingRef.current = false;
      close();
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : t("postParser.result.failed"));
    } finally {
      submittingRef.current = false;
      setSubmitting(false);
    }
  };
  return (
    <Modal
      visible={visible}
      title={t("postParser.purchaseBatch.title")}
      size="lg"
      classNames={{ footer: "flex-wrap gap-2" }}
      hideCloseButton={submitting}
      isDismissable={!submitting}
      isKeyboardDismissDisabled={submitting}
      onClose={close}
      onDestroyed={onDestroyed}
      footer={
        <>
          <Button
            ref={cancelButton}
            className="shrink-0"
            autoFocus
            variant="flat"
            isDisabled={submitting}
            onPress={close}
          >
            {t("postParser.purchaseBatch.cancel")}
          </Button>
          <Button
            color="warning"
            className="h-auto min-w-0 max-w-full whitespace-normal py-2"
            variant="solid"
            isDisabled={!valid || submitting}
            isLoading={submitting}
            startContent={<AiOutlineUnlock aria-hidden />}
            onPress={() => void confirm()}
          >
            {t("postParser.purchaseBatch.confirm", {
              count: actionableCount,
              total: totalLabel,
            })}
          </Button>
        </>
      }
    >
      <div className="space-y-4 text-sm leading-relaxed">
        <TaskFilterScopeSummary {...scope} />
        <dl className="grid grid-cols-[minmax(0,1fr)_auto] gap-x-4 gap-y-2 rounded-lg bg-warning/10 p-3">
          <dt>{t("postParser.purchaseBatch.matched")}</dt>
          <dd className="font-medium tabular-nums">{matchedCount}</dd>
          <dt>{t("postParser.purchaseBatch.actionable")}</dt>
          <dd className="font-medium tabular-nums">{actionableCount}</dd>
          <dt>{t("postParser.purchaseScope.items")}</dt>
          <dd className="font-medium tabular-nums">{purchaseItemCount}</dd>
          <dt>{t("postParser.purchaseBatch.cost")}</dt>
          <dd className="font-medium tabular-nums">{totalLabel}</dd>
        </dl>
        {Object.entries(skipReasonCounts).some(([, count]) => count > 0) && (
          <div className="space-y-2">
            <p className="font-medium">
              {t("postParser.purchaseBatch.skipped", { count: matchedCount - actionableCount })}
            </p>
            <ul className="space-y-1 text-default-500">
              {Object.entries(skipReasonCounts)
                .filter(([, count]) => count > 0)
                .map(([code, count]) => (
                  <li key={code} className="flex items-start justify-between gap-4">
                    <span>{purchaseSkipReasonLabel(t, code)}</span>
                    <span className="shrink-0 tabular-nums">{count}</span>
                  </li>
                ))}
            </ul>
          </div>
        )}
        {!actionableCount && (
          <p role="status" className="text-warning-700">
            {t("postParser.purchaseBatch.none")}
          </p>
        )}
        <p>
          {currencies.length > 1
            ? t("postParser.purchaseBatch.multipleCurrencyLimits")
            : t("postParser.purchaseBatch.limits", {
                limit: autoBuyThreshold,
                reserve: minimumRemainingCoins,
                currency,
              })}
        </p>
        <p>{t("postParser.purchaseBatch.snapshot")}</p>
        <p className="text-xs text-default-500">{t("postParser.purchaseBatch.recheck")}</p>
        {error && (
          <p role="alert" className="break-words text-danger">
            {error}
          </p>
        )}
      </div>
    </Modal>
  );
}
