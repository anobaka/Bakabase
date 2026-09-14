import type { ThirdPartyId } from "@/sdk/constants";

import { SelectItem } from "@heroui/react";
import { useTranslation } from "react-i18next";
import { AiOutlineReload, AiOutlineSearch } from "react-icons/ai";

import { Button, Input, Select } from "@/components/bakaui";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { DownloadTaskStatus, downloadTaskStatuses } from "@/sdk/constants";
import { isThirdPartyDeveloping } from "@/pages/downloader/models";

export type DownloadTaskFilter = {
  status?: DownloadTaskStatus;
  keyword?: string;
  thirdPartyId?: ThirdPartyId;
};

type Props = {
  value: DownloadTaskFilter;
  onChange: (value: DownloadTaskFilter) => void;
  sources: { value: ThirdPartyId; label: string }[];
  countsByThirdParty: Map<number, number>;
  countsByStatus: Map<number, number>;
  total: number;
};

const statusDots: Record<DownloadTaskStatus, string> = {
  [DownloadTaskStatus.Idle]: "bg-default-400",
  [DownloadTaskStatus.InQueue]: "bg-default-400",
  [DownloadTaskStatus.Starting]: "bg-warning",
  [DownloadTaskStatus.Downloading]: "bg-primary",
  [DownloadTaskStatus.Stopping]: "bg-warning",
  [DownloadTaskStatus.Complete]: "bg-success",
  [DownloadTaskStatus.Failed]: "bg-danger",
  [DownloadTaskStatus.Disabled]: "bg-default-300",
};

export const DownloadTaskFilters = ({
  value,
  onChange,
  sources,
  countsByThirdParty,
  countsByStatus,
  total,
}: Props) => {
  const { t } = useTranslation();
  const hasFilters =
    value.thirdPartyId !== undefined || value.status !== undefined || !!value.keyword;
  const selectedSource = sources.find((source) => source.value === value.thirdPartyId);
  const sourceOptions = [
    {
      value: "all",
      textValue: t<string>("downloader.filter.allSources"),
      label: (
        <span className="flex w-full items-center justify-between gap-3">
          <span>{t<string>("downloader.filter.allSources")}</span>
          <span className="text-xs tabular-nums text-default-400">{total}</span>
        </span>
      ),
    },
    ...sources.map((source) => ({
      value: String(source.value),
      textValue: source.label,
      label: (
        <span className="flex w-full min-w-0 items-center gap-2">
          <span aria-hidden className="shrink-0">
            <ThirdPartyIcon thirdPartyId={source.value} />
          </span>
          <span className="min-w-0 truncate">{source.label}</span>
          {isThirdPartyDeveloping(source.value) && (
            <span className="text-xs text-default-400">
              {t<string>("downloader.filter.developing")}
            </span>
          )}
          <span className="ml-auto text-xs tabular-nums text-default-400">
            {countsByThirdParty.get(source.value) ?? 0}
          </span>
        </span>
      ),
    })),
  ];

  return (
    <div className="min-w-0 rounded-2xl bg-default-50/60 p-3 sm:p-4">
      <div className="flex min-w-0 flex-wrap items-center gap-2">
        <Input
          isClearable
          aria-label={t<string>("downloader.filter.keyword")}
          className="min-w-0 flex-[1_1_240px] sm:max-w-md"
          classNames={{ inputWrapper: "h-10 min-h-10 bg-content1 shadow-none" }}
          placeholder={t<string>("downloader.filter.searchPlaceholder")}
          radius="lg"
          size="sm"
          startContent={
            <AiOutlineSearch aria-hidden className="shrink-0 text-lg text-default-400" />
          }
          value={value.keyword ?? ""}
          onClear={() => onChange({ ...value, keyword: undefined })}
          onValueChange={(keyword) => onChange({ ...value, keyword: keyword || undefined })}
        />
        <Select
          disallowEmptySelection
          aria-label={t<string>("downloader.filter.source")}
          className="min-w-0 flex-[1_1_200px] sm:max-w-[220px]"
          classNames={{ trigger: "h-10 min-h-10 bg-content1 shadow-none" }}
          dataSource={sourceOptions}
          radius="lg"
          renderValue={() => (
            <span className="flex min-w-0 items-center gap-2">
              {selectedSource && (
                <span aria-hidden className="shrink-0">
                  <ThirdPartyIcon thirdPartyId={selectedSource.value} />
                </span>
              )}
              <span className="truncate">
                {selectedSource?.label ?? t<string>("downloader.filter.allSources")}
              </span>
            </span>
          )}
          selectedKeys={[value.thirdPartyId === undefined ? "all" : String(value.thirdPartyId)]}
          size="sm"
          onSelectionChange={(keys) => {
            const selected = Array.from(keys)[0];

            onChange({
              ...value,
              thirdPartyId:
                selected === "all" || selected === undefined
                  ? undefined
                  : (Number(selected) as ThirdPartyId),
            });
          }}
        >
          {sourceOptions.map((source) => (
            <SelectItem
              key={source.value}
              aria-label={source.textValue}
              textValue={source.textValue}
            >
              {source.label}
            </SelectItem>
          ))}
        </Select>
        {hasFilters && (
          <Button
            className="h-10 shrink-0 text-default-500"
            size="sm"
            startContent={<AiOutlineReload aria-hidden className="text-base" />}
            variant="light"
            onPress={() => onChange({})}
          >
            {t<string>("downloader.filter.reset")}
          </Button>
        )}
      </div>
      <div
        aria-label={t<string>("downloader.filter.status")}
        className="mt-3 flex flex-wrap items-center gap-1.5"
        role="group"
      >
        <Button
          aria-pressed={value.status === undefined}
          className="min-w-0 gap-2 px-3"
          color={value.status === undefined ? "primary" : "default"}
          radius="full"
          size="sm"
          variant={value.status === undefined ? "flat" : "light"}
          onPress={() => onChange({ ...value, status: undefined })}
        >
          {t<string>("downloader.filter.allStatuses")}
          <span className="text-xs tabular-nums opacity-60">{total}</span>
        </Button>
        {downloadTaskStatuses.map((status) => {
          const selected = value.status === status.value;

          return (
            <Button
              key={status.value}
              aria-pressed={selected}
              className="min-w-0 gap-2 px-3"
              color={selected ? "primary" : "default"}
              radius="full"
              size="sm"
              variant={selected ? "flat" : "light"}
              onPress={() => onChange({ ...value, status: status.value })}
            >
              <span
                aria-hidden
                className={`h-1.5 w-1.5 shrink-0 rounded-full ${statusDots[status.value]}`}
              />
              {t<string>(`downloader.filter.status.${status.label}`)}
              <span className="text-xs tabular-nums opacity-60">
                {countsByStatus.get(status.value) ?? 0}
              </span>
            </Button>
          );
        })}
      </div>
    </div>
  );
};

export default DownloadTaskFilters;
