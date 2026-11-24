import { useTranslation } from "react-i18next";

import { reportedRate } from "./model";
import type { Metric, Rates } from "./model";

export default function RateValue({ rates, metric }: { rates?: Rates | null; metric: Metric }) {
  const { t } = useTranslation();
  const { text, qualifier: key } = reportedRate(rates, metric);
  const qualifier = key && t(key);

  return (
    <span title={qualifier}>
      {text}
      {qualifier && <span className="sr-only"> {qualifier}</span>}
    </span>
  );
}
