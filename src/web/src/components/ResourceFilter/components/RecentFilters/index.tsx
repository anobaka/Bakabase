"use client";

import type { SearchFilter } from "../../models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { PlusOutlined, ReloadOutlined } from "@ant-design/icons";

import { useFilterConfig } from "../../context/FilterContext";
import Filter from "../Filter";

import { Button, Spinner } from "@/components/bakaui";

interface IProps {
  onSelectFilter?: (filter: SearchFilter) => void;
}

const RecentFilters = ({ onSelectFilter }: IProps) => {
  const { t } = useTranslation();
  const config = useFilterConfig();
  const [recentFilters, setRecentFilters] = useState<SearchFilter[]>([]);
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [retry, setRetry] = useState(0);

  useEffect(() => {
    let active = true;
    setLoading(true);
    setFailed(false);
    void config.api
      .getRecentFilters()
      .then((filters) => {
        if (active) setRecentFilters(filters ?? []);
      })
      .catch(() => {
        if (active) setFailed(true);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [config.api, retry]);

  if (loading) {
    return (
      <div className="flex items-center gap-2 py-3 text-xs text-default-400" role="status">
        <Spinner size="sm" />
        {t<string>("resourceFilter.recent.loading")}
      </div>
    );
  }

  if (failed) {
    return (
      <div
        className="flex flex-wrap items-center justify-between gap-2 py-2 text-xs text-default-500"
        role="alert"
      >
        <span>{t<string>("resourceFilter.recent.failed")}</span>
        <Button
          size="sm"
          variant="light"
          startContent={<ReloadOutlined />}
          onPress={() => setRetry((value) => value + 1)}
        >
          {t<string>("resourceFilter.recent.retry")}
        </Button>
      </div>
    );
  }

  if (recentFilters.length === 0) {
    return (
      <p className="py-2 text-xs leading-relaxed text-default-400">
        {t<string>("resourceFilter.recent.empty")}
      </p>
    );
  }

  return (
    <div className="flex max-h-64 min-w-0 flex-col gap-1.5 overflow-y-auto">
      {recentFilters.map((filter, index) => (
        <div
          key={index}
          className="flex min-w-0 items-center gap-2 rounded-lg bg-default-50 px-2 py-1.5"
        >
          <div className="min-w-0 flex-1 overflow-x-auto">
            <Filter isReadonly removeBackground filter={filter} />
          </div>
          <Button
            isIconOnly
            aria-label={t<string>("resourceFilter.recent.add")}
            className="shrink-0"
            color="primary"
            size="sm"
            variant="light"
            onPress={() => onSelectFilter?.(filter)}
          >
            <PlusOutlined className="text-sm" />
          </Button>
        </div>
      ))}
    </div>
  );
};

RecentFilters.displayName = "RecentFilters";

export default RecentFilters;
