import type { PostContentSnapshot } from "@/core/models/PostParserTask";

import { useTranslation } from "react-i18next";
import { Accordion, AccordionItem, Chip } from "@/components/bakaui";
import { PostParserSource } from "@/sdk/constants";
import { translateKnownMessage } from "../knownMessages";

type ContentLock = PostContentSnapshot["locks"][number];

/** Source-provided currency names take precedence; an unknown source never implies coins. */
export function sourceCurrencyLabel(
  currencyName?: string | null,
  currencyCode?: string | null,
  source?: PostParserSource | 0,
  unknownLabel = "—",
): string {
  return (
    currencyName?.trim() ||
    currencyCode?.trim() ||
    (source === PostParserSource.SoulPlus ? "SP币" : unknownLabel)
  );
}

/** Keep decimal text intact: converting it to a JS number can change the approved amount. */
export function formatSourceAmount(
  value?: string | number | null,
  fallbackValue?: number | null,
  unknownLabel = "—",
): string {
  if (typeof value === "string" && value.trim()) return value.trim();
  if (typeof value === "number" && Number.isFinite(value)) return String(value);
  if (typeof fallbackValue === "number" && Number.isFinite(fallbackValue))
    return String(fallbackValue);
  return unknownLabel;
}

// Compare decimal representations without losing large integers or fractional digits.
function priceIdentity(value: string): string {
  const decimal = /^([+-]?)(\d+)(?:\.(\d+))?$/.exec(value);
  if (!decimal) return value;
  const integer = decimal[2].replace(/^0+(?=\d)/, "");
  const fraction = (decimal[3] ?? "").replace(/0+$/, "");
  const sign = decimal[1] === "-" && (integer !== "0" || fraction) ? "-" : "";
  return `${sign}${integer}${fraction ? `.${fraction}` : ""}`;
}

const relevanceValues = new Set(["required", "optional", "unknown"]);
const purposeValues = new Set([
  "purchaseProof",
  "donation",
  "unrelated",
  "supplement",
  "downloadCredentials",
  "processingInstructions",
  "unknown",
]);

/** Evidence about page blocks only. This component does not construct or approve a purchase quote. */
export default function LockedContentDetails({
  content,
  source,
  compact = false,
}: {
  content: PostContentSnapshot;
  source?: PostParserSource | 0;
  compact?: boolean;
}) {
  const { t } = useTranslation();
  const locks = content.locks.filter((lock) => !lock.isBought);
  const pricesByUrl = new Map<string, Set<string>>();
  for (const lock of locks) {
    const price = formatSourceAmount(lock.priceText, lock.price);
    if (!lock.url || price === "—") continue;
    const prices = pricesByUrl.get(lock.url) ?? new Set<string>();
    prices.add(priceIdentity(price));
    pricesByUrl.set(lock.url, prices);
  }
  const conflictingPrices = [...pricesByUrl.values()].some((prices) => prices.size > 1);

  const renderLocks = (items: ContentLock[], summarized: boolean) => (
    <ol className="space-y-2">
      {items.map((lock, index) => {
        // A purchase endpoint can serve several page blocks. Never use its URL as a block identity.
        const decisions = lock.blockId
          ? content.readiness?.decisions.filter((decision) => decision.blockId === lock.blockId)
          : undefined;
        const decision = decisions?.length === 1 ? decisions[0] : undefined;
        const relevance =
          decision && relevanceValues.has(decision.relevance) ? decision.relevance : "unknown";
        const purpose =
          decision && purposeValues.has(decision.purpose) ? decision.purpose : "unknown";
        const lockCurrencyName = lock.currencyName?.trim();
        const lockCurrencyCode = lock.currencyCode?.trim();
        const currency = sourceCurrencyLabel(
          lockCurrencyName || (!lockCurrencyCode ? content.currencyName : undefined),
          lockCurrencyCode || content.currencyCode,
          source,
          t("postParser.lockedContent.currencyUnknown"),
        );
        return (
          <li
            key={`${lock.blockId ?? lock.id ?? "block"}-${index}`}
            className="min-w-0 space-y-2 rounded-lg border border-default-200 bg-default-50 p-2"
          >
            <div className="flex min-w-0 flex-wrap items-center gap-2">
              <span className="text-xs text-default-500">
                {t("postParser.lockedContent.price")}
              </span>
              <span className="break-all font-medium tabular-nums">
                {formatSourceAmount(
                  lock.priceText,
                  lock.price,
                  t("postParser.lockedContent.priceUnknown"),
                )}{" "}
                {currency}
              </span>
              <Chip
                size="sm"
                variant="flat"
                color={relevance === "required" ? "warning" : "default"}
              >
                {t(`postParser.lockedContent.relevance.${relevance}`)}
              </Chip>
              <span className="text-xs text-default-500">
                {t("postParser.lockedContent.purpose")}：
                {t(`postParser.lockedContent.purpose.${purpose}`)}
              </span>
            </div>
            {summarized ? (
              lock.contextText?.trim() && (
                <p className="line-clamp-1 break-words text-xs text-default-500">
                  {lock.contextText.trim()}
                </p>
              )
            ) : (
              <dl className="grid min-w-0 grid-cols-[auto_minmax(0,1fr)] gap-x-2 gap-y-1 text-xs">
                <dt className="text-default-500">{t("postParser.lockedContent.blockId")}</dt>
                <dd className="break-all">{lock.blockId || "—"}</dd>
                <dt className="text-default-500">{t("postParser.lockedContent.floor")}</dt>
                <dd>{lock.floor || "—"}</dd>
                <dt className="text-default-500">{t("postParser.lockedContent.context")}</dt>
                <dd className="whitespace-pre-wrap break-words">
                  {lock.contextText?.trim() || t("postParser.lockedContent.contextUnknown")}
                </dd>
                {decision?.evidence.length ? (
                  <>
                    <dt className="text-default-500">{t("postParser.lockedContent.evidence")}</dt>
                    <dd>
                      <ul className="space-y-1">
                        {decision.evidence.map((evidence, evidenceIndex) => (
                          <li key={evidenceIndex} className="whitespace-pre-wrap break-words">
                            {evidence}
                          </li>
                        ))}
                      </ul>
                    </dd>
                  </>
                ) : null}
                {decision?.reason && (
                  <>
                    <dt className="text-default-500">{t("postParser.lockedContent.reason")}</dt>
                    <dd className="whitespace-pre-wrap break-words">
                      {translateKnownMessage(t, decision.reason)}
                    </dd>
                  </>
                )}
              </dl>
            )}
            {!summarized && !decision && (
              <p className="text-xs text-default-500">{t("postParser.lockedContent.unassessed")}</p>
            )}
          </li>
        );
      })}
    </ol>
  );

  return (
    <section aria-label={t("postParser.lockedContent.title")} className="min-w-0 space-y-2 text-sm">
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
        <h3 className="font-medium">
          {t("postParser.lockedContent.summary", { count: locks.length })}
        </h3>
        {content.readiness && (
          <span className="text-xs text-default-500">
            {t("postParser.lockedContent.basis")}：
            {t(`postParser.lockedContent.basis.${content.readiness.basis}`)}
          </span>
        )}
      </div>
      <p className="text-xs leading-relaxed text-default-500">
        {t("postParser.lockedContent.displayOnly")}
      </p>
      {conflictingPrices && (
        <p role="alert" className="text-xs leading-relaxed text-warning-700">
          {t("postParser.lockedContent.conflictingPrices")}
        </p>
      )}
      {!locks.length ? (
        <p className="text-xs text-default-500">{t("postParser.lockedContent.none")}</p>
      ) : compact ? (
        <>
          {renderLocks(locks.slice(0, 3), true)}
          <Accordion isCompact keepContentMounted={false} className="px-0">
            <AccordionItem
              key="complete"
              title={t("postParser.lockedContent.details", { count: locks.length })}
            >
              {renderLocks(locks, false)}
            </AccordionItem>
          </Accordion>
        </>
      ) : (
        renderLocks(locks, false)
      )}
    </section>
  );
}
