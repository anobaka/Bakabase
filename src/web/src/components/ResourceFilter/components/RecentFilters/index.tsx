"use client";

import type { SearchFilter } from "../../models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { Button } from "@heroui/react";
import { AiOutlineRightCircle } from "react-icons/ai";

import { useFilterConfig } from "../../context/FilterContext";
import Filter from "../Filter";

import { buildLogger } from "@/components/utils";
import { Tooltip } from "@/components/bakaui";

interface IProps {
  onSelectFilter?: (filter: SearchFilter) => void;
}

const log = buildLogger("RecentFilters");

const RecentFilters = ({ onSelectFilter }: IProps) => {
  const { t } = useTranslation();
  const config = useFilterConfig();
  const [recentFilters, setRecentFilters] = useState<SearchFilter[]>([]);

  const loadRecentFilters = async () => {
    try {
      const filters = await config.api.getRecentFilters();
      setRecentFilters(filters || []);
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
        <Tooltip
          key={index}
          content={t("Apply this filter to the current search")}
          placement="right"
        >
          <Button
            className="flex items-center justify-between gap-1"
            variant="light"
            size="sm"
            color="primary"
            onPress={() => onSelectFilter?.(filter)}
          >
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
