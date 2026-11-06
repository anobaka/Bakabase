import { useTranslation } from "react-i18next";
import { FiGlobe } from "react-icons/fi";
import { TbMagnet } from "react-icons/tb";
import { Button, Input } from "@/components/bakaui";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import {
  buildCenterRows,
  centerStatuses,
  type CenterFilter,
  type CenterTask,
} from "../downloadCenter";
import { ThirdPartyId } from "@/sdk/constants";

export default function DownloadCenterFilters({
  value,
  onChange,
  tasks,
  roots,
}: {
  value: CenterFilter;
  onChange: (value: CenterFilter) => void;
  tasks: CenterTask[];
  roots: CenterTask[];
}) {
  const { t } = useTranslation();
  const categories = ["all", "general", "site"] as const;
  const sites = [
    ...new Set(
      tasks
        .filter((task) => task.category === "site" && task.thirdPartyId != null)
        .map((task) => task.thirdPartyId!),
    ),
  ].sort((a, b) => a - b);
  const providers = [
    { value: undefined, label: t("downloader.filter.allSources"), icon: undefined },
    ...(value.category !== "site"
      ? [
          { value: "bt", label: t("downloadCenter.provider.bt"), icon: <TbMagnet size={17} /> },
          { value: "http", label: t("downloadCenter.provider.http"), icon: <FiGlobe size={17} /> },
        ]
      : []),
    ...(value.category !== "general"
      ? sites.map((id) => ({
          value: `site:${id}`,
          label: t(ThirdPartyId[id]),
          icon: <ThirdPartyIcon thirdPartyId={id} />,
        }))
      : []),
  ];
  const filteredScope = buildCenterRows(
    tasks,
    { category: value.category, provider: value.provider },
    new Set(),
  ).filtered;
  const statusCounts = new Map<string, number>();
  for (const task of filteredScope)
    statusCounts.set(task.status, (statusCounts.get(task.status) ?? 0) + 1);
  return (
    <div className="rounded-2xl bg-default-50/60 p-3 sm:p-4">
      <div
        role="group"
        aria-label={t("downloadCenter.category.label")}
        className="mb-3 flex flex-wrap gap-2"
      >
        {categories.map((category) => (
          <Button
            key={category}
            aria-pressed={value.category === category}
            color={value.category === category ? "primary" : "default"}
            variant={value.category === category ? "flat" : "light"}
            size="sm"
            onPress={() => onChange({ ...value, category, provider: undefined, id: undefined })}
          >
            {t(`downloadCenter.category.${category}`)}
            <span className="text-xs opacity-60">
              {category === "all"
                ? roots.length
                : category === "general"
                  ? tasks.filter((task) => task.category === "general").length
                  : roots.filter((task) => task.category === "site").length}
            </span>
          </Button>
        ))}
      </div>
      <div className="flex flex-wrap items-center gap-2">
        <Input
          isClearable
          aria-label={t("downloader.filter.keyword")}
          className="min-w-40 flex-1"
          size="sm"
          placeholder={t("downloadCenter.search")}
          value={value.keyword ?? ""}
          onValueChange={(keyword) => onChange({ ...value, keyword })}
          onClear={() => onChange({ ...value, keyword: undefined })}
        />
        {(value.status || value.keyword || value.provider || value.id) && (
          <Button size="sm" variant="light" onPress={() => onChange({ category: value.category })}>
            {t("downloader.filter.reset")}
          </Button>
        )}
      </div>
      <div
        role="group"
        aria-label={t("downloader.filter.source")}
        className="mt-3 flex flex-wrap gap-1"
      >
        {providers.map(({ value: provider, label, icon }) => (
          <Button
            key={provider ?? "all"}
            aria-pressed={value.provider === provider}
            color={value.provider === provider ? "primary" : "default"}
            size="sm"
            startContent={icon && <span aria-hidden="true">{icon}</span>}
            variant="light"
            onPress={() => onChange({ ...value, provider, id: undefined })}
          >
            {label}
          </Button>
        ))}
      </div>
      <div
        role="group"
        aria-label={t("downloader.filter.status")}
        className="mt-3 flex flex-wrap gap-1"
      >
        <Button
          aria-pressed={!value.status}
          color={!value.status ? "primary" : "default"}
          size="sm"
          variant="light"
          onPress={() => onChange({ ...value, status: undefined })}
        >
          {t("downloader.filter.allStatuses")}
          <span className="text-xs opacity-60">{filteredScope.length}</span>
        </Button>
        {centerStatuses.map((status) => (
          <Button
            key={status}
            aria-pressed={value.status === status}
            color={value.status === status ? "primary" : "default"}
            size="sm"
            variant="light"
            onPress={() => onChange({ ...value, status })}
          >
            {t(`downloadCenter.status.${status}`)}
            <span className="text-xs opacity-60">{statusCounts.get(status) ?? 0}</span>
          </Button>
        ))}
      </div>
    </div>
  );
}
