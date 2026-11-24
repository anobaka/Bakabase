import { lazy, Suspense, useState } from "react";
import { useTranslation } from "react-i18next";
import { LuDownload, LuFileEdit, LuFileSearch } from "react-icons/lu";

import { metrics, reportedRate, sampleTime } from "./model";
import useAcquisitionThroughput from "./useAcquisitionThroughput";
import { Button, Modal, Spinner, Tooltip } from "@/components/bakaui";

const ThroughputHistory = lazy(() => import("./ThroughputHistory"));
const metricIcons = { download: LuDownload, read: LuFileSearch, write: LuFileEdit } as const;

export default function AcquisitionThroughput() {
  const { t, i18n } = useTranslation();
  const [open, setOpen] = useState(false);
  const { snapshot, disconnected, delayed, receivedHistory } = useAcquisitionThroughput(open);
  const latest = snapshot?.latest;
  const rates = disconnected || delayed ? undefined : latest?.totals;
  const last = latest && sampleTime(latest);
  const sampledAt =
    typeof last === "number" && Number.isFinite(last)
      ? new Date(last).toLocaleTimeString(i18n.language)
      : undefined;
  const status = disconnected
    ? t("acquisition.throughput.disconnected")
    : delayed
      ? t("acquisition.throughput.delayed")
      : !snapshot
        ? t("acquisition.throughput.loading")
        : !latest
          ? t("acquisition.throughput.noSamples")
          : null;

  const title = t("acquisition.throughput.title");
  const readings = metrics.map(({ key, color }) => {
    const { text, qualifier } = reportedRate(rates, key);

    return { key, color, label: t(`acquisition.throughput.${key}`), text, hint: qualifier && t(qualifier) };
  });
  const accessibleName = [
    title,
    ...readings.map(({ label, text, hint }) => [label, text, hint].filter(Boolean).join(" ")),
    status,
  ].filter(Boolean).join(". ");

  return (
    <>
      <Tooltip
        delay={400}
        placement="bottom"
        isDisabled={open}
        content={
          <div className="space-y-1 py-0.5 text-xs">
            <div className="font-medium">{title}</div>
            <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
              {readings.map(({ key, label, text, hint }) => (
                <div key={key} className="contents">
                  <dt>{label}</dt>
                  <dd className="tabular-nums">
                    {text}
                    {hint && <span className="text-default-500"> {hint}</span>}
                  </dd>
                </div>
              ))}
            </dl>
            {status && <div>{status}</div>}
          </div>
        }
      >
        <Button
          disableRipple
          size="sm"
          variant="bordered"
          type="button"
          aria-label={accessibleName}
          aria-haspopup="dialog"
          aria-expanded={open}
          onPress={() => setOpen(true)}
          className="h-auto min-h-8 min-w-0 max-w-full shrink-0 justify-start gap-0 rounded-lg border border-default-200 px-2 py-1 text-xs hover:bg-default-100 focus-visible:outline-primary"
        >
          <span aria-hidden className="flex min-w-0 max-w-full flex-wrap items-center gap-x-2.5 gap-y-1">
            {readings.map(({ key, color, text }) => {
              const Icon = metricIcons[key];

              return (
                <span key={key} className="inline-flex items-center gap-1 whitespace-nowrap font-medium tabular-nums">
                  <Icon aria-hidden className="size-4 shrink-0" style={{ color }} />
                  {text}
                </span>
              );
            })}
          </span>
        </Button>
      </Tooltip>
      {open && (
        <Modal
          visible
          title={title}
          size="5xl"
          scrollBehavior="inside"
          footer={false}
          onClose={() => setOpen(false)}
        >
          <div className="min-w-0 space-y-3 pb-3">
            <div className="flex flex-wrap justify-between gap-x-4 gap-y-1 text-xs text-default-500">
              <span>
                {t("acquisition.throughput.window", {
                  count: (snapshot?.windowSeconds || 300) / 60,
                })}
              </span>
              {sampledAt && (
                <span>{t("acquisition.throughput.sampledAt", { time: sampledAt })}</span>
              )}
            </div>
            <p className="text-xs text-default-500">
              {t("acquisition.throughput.logicalIo")} {t("acquisition.throughput.legend")}
            </p>
            {(disconnected || delayed) && (
              <p role="status" className="text-sm text-warning-600">
                {status}
              </p>
            )}
            {receivedHistory && snapshot ? (
              <Suspense
                fallback={<Spinner aria-label={t("acquisition.throughput.loading")} size="sm" />}
              >
                <ThroughputHistory snapshot={snapshot} current={!disconnected && !delayed} />
              </Suspense>
            ) : (
              !disconnected && (
                <p role="status" className="text-sm">
                  {t("acquisition.throughput.loading")}
                </p>
              )
            )}
          </div>
        </Modal>
      )}
    </>
  );
}
