import { useTranslation } from "react-i18next";
import type { PlanTaskDetail } from "./models";

/** A local revision without a delivery layout uses the plan's download directory / Acquired. */
function defaultRevisionRoot(output?: string | null) {
  if (!output?.trim()) return undefined;
  const separator = output.includes("\\") ? "\\" : "/";
  return output.replace(/[\\/]+$/, "") + separator + "Acquired";
}

export default function RevisionDeliveryRoots({
  detail, currentRoot, useCurrent, override,
}: {
  detail: PlanTaskDetail;
  /** Undefined while settings are unavailable; null or blank when the user has no explicit root. */
  currentRoot?: string | null;
  useCurrent: boolean;
  override?: string;
}) {
  const { t } = useTranslation();
  const saved = detail.plan?.deliveryLayout?.rootDirectory || undefined;
  const published = saved ? [] : [...new Set(detail.replanArtifacts
    .filter(a => a.kind === "publishedFiles" && a.canRepublish && a.rootDirectory)
    .map(a => a.rootDirectory))];
  const fallback = defaultRevisionRoot(detail.plan?.outputDirectory);
  const current = currentRoot === undefined ? undefined : currentRoot || fallback;
  const next = override === undefined ? (useCurrent ? current : saved || fallback) : override;
  const unknown = t("acquisition.revisionRoot.unavailable");
  return (
    <dl className="grid min-w-0 grid-cols-1 gap-1 text-xs sm:grid-cols-[auto_minmax(0,1fr)] sm:gap-x-3">
      <dt className="text-default-500">
        {t(saved ? "acquisition.revisionRoot.saved" : "acquisition.revisionRoot.published")}
      </dt>
      <dd className="break-all font-mono">
        {saved || (published.length ? published.map(path => <div key={path}>{path}</div>) : unknown)}
      </dd>
      <dt className="text-default-500">{t("acquisition.revisionRoot.current")}</dt>
      <dd className="break-all font-mono">{currentRoot === undefined ? unknown :
        currentRoot || t("acquisition.revisionRoot.downloadDefault")}</dd>
      <dt className="font-medium">{t("acquisition.revisionRoot.next")}</dt>
      <dd aria-live="polite" className={`break-all font-mono ${current && next && next !== current ? "text-warning-700" : "font-medium"}`}>
        {next || (override === "" ? t("acquisition.delivery.chooseRoot") : unknown)}
      </dd>
    </dl>
  );
}
