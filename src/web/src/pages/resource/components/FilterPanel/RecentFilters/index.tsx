"use client";

import type { ResourceSearchFilter } from "../FilterGroupsPanel/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { Button, Chip } from "@heroui/react";
import { AiOutlineRightCircle, AiOutlineSelect } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { buildLogger } from "@/components/utils";
import Filter from "@/pages/resource/components/FilterPanel/FilterGroupsPanel/FilterGroup/Filter";
import { Tooltip } from "@/components/bakaui";

interface IProps {
  onSelectFilter?: (filter: ResourceSearchFilter) => void;
}

const log = buildLogger("RecentFilters");
const RecentFilters = ({ onSelectFilter }: IProps) => {
  const { t } = useTranslation();
  const [recentFilters, setRecentFilters] = useState<ResourceSearchFilter[]>(
    [],
  );
  const loadRecentFilters = async () => {
    try {
      const response = await BApi.options.getRecentResourceFilters();

      setRecentFilters(response.data || []);
    } catch (error) {
      console.error("Failed to load recent filters:", error);
    }
  };

  useEffect(() => {
    loadRecentFilters();
  }, []);

  if (recentFilters.length === 0) {
    return null;
  }

  return (
    <div className={"grid-cols-2 gap-1"}>
      {recentFilters.map((filter, index) => (
        <Tooltip content={t("Apply this filter to the current search")} placement="right">
          <Button key={index} className="flex items-center justify-between gap-1" variant="light" size="sm" color="primary" onPress={() => onSelectFilter?.(filter)}>
            <Filter isReadonly filter={filter} removeBackground />
            <AiOutlineRightCircle className={"text-lg"} />
          </Button>
        </Tooltip>
      ))}
    </div>
  );
};

RecentFilters.displayName = "RecentFilters";

export default RecentFilters;
