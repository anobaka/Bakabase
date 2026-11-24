import { lazy, Suspense, useState } from "react";
import { useTranslation } from "react-i18next";
import { LuActivity, LuChevronRight } from "react-icons/lu";

import { metrics, sampleTime } from "./model";
import RateValue from "./RateValue";
import useAcquisitionThroughput from "./useAcquisitionThroughput";
import { Button, Modal, Spinner } from "@/components/bakaui";

const ThroughputHistory = lazy(() => import("./ThroughputHistory"));

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

  return (
    <>
      <Button
        fullWidth
        disableRipple
        variant="bordered"
        type="button"
        aria-haspopup="dialog"
        aria-expanded={open}
        onPress={() => setOpen(true)}
        className="grid h-auto min-w-0 max-w-full grid-cols-[1fr_auto] items-center justify-start whitespace-normal gap-x-5 gap-y-2 self-start rounded-lg border border-default-200 px-3 py-2 text-left text-sm transition-colors hover:bg-default-100 focus-visible:outline-primary sm:flex sm:w-fit sm:flex-wrap"
      >
        <span className="inline-flex items-center gap-1.5 font-medium">
          <LuActivity aria-hidden />
          {t("acquisition.throughput.title")}
        </span>
        <span className="col-span-2 row-start-2 grid grid-cols-3 gap-x-3 sm:flex sm:gap-x-5">
          {metrics.map((metric) => {
            return (
              <span
                key={metric.key}
                className="inline-flex flex-col gap-0.5 whitespace-nowrap sm:flex-row sm:items-baseline sm:gap-1.5"
              >
                <span className="text-xs text-default-500">
                  {t(`acquisition.throughput.${metric.key}`)}
                </span>
                <span className="font-medium tabular-nums">
                  <RateValue rates={rates} metric={metric.key} />
                </span>
              </span>
            );
          })}
        </span>
        {status && <span className="col-span-2 text-xs text-default-500">{status}</span>}
        <LuChevronRight aria-hidden className="col-start-2 row-start-1 ml-auto text-default-500" />
      </Button>
      {open && (
        <Modal
          visible
          title={t("acquisition.throughput.title")}
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
