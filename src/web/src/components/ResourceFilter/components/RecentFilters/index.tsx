"use client";

import type { SearchFilter } from "../../models";

import { useTranslation } from "react-i18next";
import { useEffect, useState } from "react";
import { Button } from "@heroui/react";
import { BiAddToQueue } from "react-icons/bi";

import { useFilterConfig } from "../../context/FilterContext";
import Filter from "../Filter";

import { buildLogger } from "@/components/utils";
import { Spinner } from "@/components/bakaui";

interface IProps {
  onSelectFilter?: (filter: SearchFilter) => void;
}

const log = buildLogger("RecentFilters");

const RecentFilters = ({ onSelectFilter }: IProps) => {
  const { t } = useTranslation();
  const config = useFilterConfig();
  const [recentFilters, setRecentFilters] = useState<SearchFilter[]>([]);
  const [loading, setLoading] = useState(true);

  const loadRecentFilters = async () => {
    setLoading(true);
    try {
      const filters = await config.api.getRecentFilters();

      setRecentFilters(filters || []);
    } catch (error) {
      console.error("Failed to load recent filters:", error);
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    loadRecentFilters();
  }, []);

  if (loading) {
    return <Spinner size="sm" />;
  }

  if (recentFilters.length === 0) {
    return null;
  }

  return (
    <div className={"grid-cols-2 gap-1 max-h-[400px] overflow-y-auto"}>
      {recentFilters.map((filter, index) => (
        <div key={index} className="flex items-center gap-1">
          <Button
            isIconOnly
            color="primary"
            size="sm"
            variant="light"
            onPress={() => onSelectFilter?.(filter)}
          >
            <BiAddToQueue className={"text-lg"} />
          </Button>
          <Filter isReadonly removeBackground filter={filter} />
        </div>
      ))}
    </div>
  );
};

RecentFilters.displayName = "RecentFilters";

export default RecentFilters;
