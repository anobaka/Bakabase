"use client";

import type {
  ComparisonPlanViewModel,
  ComparisonResultGroupViewModel,
} from "@/sdk/constants";

import { useTranslation } from "react-i18next";
import { useCallback, useEffect, useState } from "react";
import { InfoCircleOutlined } from "@ant-design/icons";

import {
  Chip,
  Pagination,
  Spinner,
  Checkbox,
  NumberInput,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import ComparisonResultGroup from "./ComparisonResultGroup";

interface ComparisonResultsProps {
  plan: ComparisonPlanViewModel;
}

const GROUPS_PAGE_SIZE = 10;

const ComparisonResults = ({ plan }: ComparisonResultsProps) => {
  const { t } = useTranslation();

  const [groups, setGroups] = useState<ComparisonResultGroupViewModel[]>([]);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [pageCount, setPageCount] = useState(0);
  const [hiddenCount, setHiddenCount] = useState(0);
  const [minMemberCount, setMinMemberCount] = useState(2);
  const [includeHidden, setIncludeHidden] = useState(false);

  const loadResults = useCallback(async () => {
    setLoading(true);
    try {
      const r = await BApi.comparison.searchComparisonResults(plan.id!, {
        pageIndex: page - 1,
        pageSize: GROUPS_PAGE_SIZE,
        minMemberCount,
        includeHidden,
      });
      setGroups(r.data || []);
      setPageCount(r.totalCount || 0);
      // Use hiddenCount from API response
      setHiddenCount((r as any).hiddenCount || 0);
    } finally {
      setLoading(false);
    }
  }, [plan.id, page, minMemberCount, includeHidden]);

  useEffect(() => {
    loadResults();
  }, [loadResults]);

  const handleGroupHiddenChange = useCallback((groupId: number, hidden: boolean) => {
    if (hidden) {
      // Group was hidden
      if (!includeHidden) {
        // Remove from list when not including hidden groups
        setGroups((prev) => prev.filter((g) => g.id !== groupId));
        setPageCount((prev) => Math.max(0, prev - 1));
      } else {
        // Update the group's isHidden state in place
        setGroups((prev) => prev.map((g) =>
          g.id === groupId ? { ...g, isHidden: true } as any : g
        ));
      }
      setHiddenCount((prev) => prev + 1);
    } else {
      // Group was unhidden
      // Update the group's isHidden state in place
      setGroups((prev) => prev.map((g) =>
        g.id === groupId ? { ...g, isHidden: false } as any : g
      ));
      setHiddenCount((prev) => Math.max(0, prev - 1));
    }
  }, [includeHidden]);

  // Total groups = current page count + hidden count (when not including hidden)
  const totalGroups = includeHidden ? pageCount : pageCount + hiddenCount;
  const totalPages = Math.ceil(pageCount / GROUPS_PAGE_SIZE);

  return (
    <div className="flex flex-col gap-2">
      {/* Filters */}
      <div className="flex items-center gap-4 flex-wrap">
        <div className="flex items-center gap-2">
          <span className="text-sm text-default-500">{t("comparison.label.minMemberCount")}:</span>
          <NumberInput
            className="w-20"
            min={1}
            size="sm"
            value={minMemberCount}
            onValueChange={(v) => {
              setMinMemberCount(v ?? 2);
              setPage(1);
            }}
          />
        </div>
        <Checkbox
          size="sm"
          isSelected={includeHidden}
          onValueChange={(v) => {
            setIncludeHidden(v);
            setPage(1);
          }}
        >
          {t("comparison.label.includeHidden")}
        </Checkbox>
        <Chip size="sm" variant="flat">
          {t("comparison.label.totalGroups")}: {totalGroups}
        </Chip>
        {hiddenCount > 0 && (
          <Chip size="sm" variant="flat" color="warning">
            {t("comparison.label.hiddenGroups")}: {hiddenCount}
          </Chip>
        )}
        <Chip size="sm" variant="flat" className="text-default-400">
          {t("comparison.label.threshold")}: {plan.threshold}%
        </Chip>
      </div>

      {/* Hint */}
      <div className="flex items-center gap-2 text-sm text-default-500 bg-default-100 rounded-lg px-3 py-2">
        <InfoCircleOutlined className="text-primary" />
        <span>{t("comparison.hint.rerunAfterEdit")}</span>
      </div>

      {/* Results */}
      {loading ? (
        <div className="flex items-center justify-center min-h-[300px]">
          <Spinner size="lg" />
        </div>
      ) : groups.length === 0 ? (
        <div className="flex flex-col items-center justify-center min-h-[300px] text-default-400">
          <span>{t("comparison.empty.noResults")}</span>
        </div>
      ) : (
        <div className="flex flex-col gap-4">
          {/* Groups list */}
          {groups.map((group) => (
            <ComparisonResultGroup
              key={group.id}
              planId={plan.id!}
              group={group}
              threshold={plan.threshold!}
              onHiddenChange={(hidden) => handleGroupHiddenChange(group.id!, hidden)}
            />
          ))}

          {/* Pagination */}
          {totalPages > 1 && (
            <div className="flex justify-center mt-4">
              <Pagination
                page={page}
                total={totalPages}
                onChange={setPage}
              />
            </div>
          )}
        </div>
      )}
    </div>
  );
};

export default ComparisonResults;
