import { useTranslation } from "react-i18next";

import { formatReportedRate, partial, rate } from "./model";
import type { Metric, Rates } from "./model";

export default function RateValue({ rates, metric }: { rates?: Rates | null; metric: Metric }) {
  const { t } = useTranslation();
  const value = rate(rates, metric);
  const qualified = value !== null && partial(rates, metric);
  const qualifier = qualified
    ? t(
        metric === "download"
          ? "acquisition.throughput.partialDownload"
          : "acquisition.throughput.partialIo",
      )
    : undefined;

  return (
    <span title={qualifier}>
      {formatReportedRate(metric, value, qualified)}
      {qualifier && <span className="sr-only"> {qualifier}</span>}
    </span>
  );
}
