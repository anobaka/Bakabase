"use client";

import type { ResourceSearchFilter } from "../FilterGroupsPanel/models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { Button } from "@heroui/react";
import { AiOutlineSelect } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import { buildLogger } from "@/components/utils";
import Filter from "@/pages/resource/components/FilterPanel/FilterGroupsPanel/FilterGroup/Filter";

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
        <div key={index} className="flex items-center justify-between gap-1">
          <Filter isReadonly filter={filter} />
          <div className={"flex items-center"}>
            <Button
              isIconOnly
              color={"primary"}
              size={"sm"}
              variant={"light"}
              onPress={() => onSelectFilter?.(filter)}
            >
              <AiOutlineSelect className={"text-lg"} />
            </Button>
          </div>
        </div>
      ))}
    </div>
  );
};

RecentFilters.displayName = "RecentFilters";

export default RecentFilters;
