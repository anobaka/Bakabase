import { useTranslation } from "react-i18next";

interface Target {
  id: string;
  title?: string;
  downloadTaskId?: number;
  transportTaskId?: string;
  problem?: string;
  excluded?: boolean;
  files?: string[];
  fileCount?: number;
}

export default function WorksetResults({ json }: { json?: string | null }) {
  const { t } = useTranslation();
  let targets: Target[] = [];
  try {
    const items: unknown = JSON.parse(json ?? "[]");
    if (Array.isArray(items))
      targets = items.flatMap((item: unknown) => {
        if (
          !item ||
          typeof item !== "object" ||
          !("targets" in item) ||
          !Array.isArray(item.targets)
        )
          return [];
        return item.targets.filter(
          (target: unknown): target is Target =>
            !!target &&
            typeof target === "object" &&
            "id" in target &&
            typeof target.id === "string",
        );
      });
  } catch {
    /* Other workflow output is rendered by the generic preview. */
  }
  if (!targets.length) return null;
  return (
    <div className="space-y-2" aria-label={t("cloudDownload.results")}>
      {targets.map((target) => (
        <div key={target.id} className="space-y-1 rounded-lg bg-default-50 p-3 text-sm">
          <p className="font-medium">{target.title || target.id}</p>
          <p className={target.problem ? "text-warning-600" : "text-default-500"}>
            {target.problem
              ? t(`cloudDownload.problem.${target.problem}`, { defaultValue: target.problem })
              : target.excluded
                ? t("cloudDownload.skipped")
                : t("cloudDownload.fileCount", {
                    count: target.files?.length ?? target.fileCount ?? 0,
                  })}
          </p>
          {(target.downloadTaskId != null || target.transportTaskId) && (
            <a
              className="text-primary"
              href={`#/downloader?id=${encodeURIComponent(target.transportTaskId ?? `site:${target.downloadTaskId}`)}`}
            >
              {t("cloudDownload.viewTask")}
            </a>
          )}
          {!!target.files?.length && (
            <details className="text-xs">
              <summary className="cursor-pointer">{t("cloudDownload.files")}</summary>
              <ul className="mt-1 space-y-1">
                {target.files.map((file) => (
                  <li className="break-all" key={file}>
                    {file}
                  </li>
                ))}
              </ul>
            </details>
          )}
        </div>
      ))}
    </div>
  );
}
