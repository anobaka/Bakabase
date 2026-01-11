"use client";

import type { ComparisonResultGroupViewModel } from "@/sdk/constants";
import type { Resource as ResourceModel } from "@/core/models/Resource";

import { useTranslation } from "react-i18next";
import { StarFilled, EyeInvisibleOutlined, EyeOutlined, NodeIndexOutlined } from "@ant-design/icons";
import { useCallback, useEffect, useState } from "react";

import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Chip,
  Pagination,
  Spinner,
  Tooltip,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import Resource from "@/components/Resource";
import { ResourceAdditionalItem } from "@/sdk/constants";
import ComparisonNetworkGraph from "./ComparisonNetworkGraph";

interface ComparisonResultGroupProps {
  planId: number;
  group: ComparisonResultGroupViewModel;
  threshold: number;
  onHiddenChange?: (hidden: boolean) => void;
}

const RESOURCES_PAGE_SIZE = 50;

const ComparisonResultGroup = ({ planId, group, threshold, onHiddenChange }: ComparisonResultGroupProps) => {
  const { t } = useTranslation();

  const [resources, setResources] = useState<ResourceModel[]>([]);
  const [loading, setLoading] = useState(true);
  const [page, setPage] = useState(1);
  const [allResourceIds, setAllResourceIds] = useState<number[]>([]);
  const [primaryResourceId, setPrimaryResourceId] = useState<number | null>(null);
  const [showNetworkGraph, setShowNetworkGraph] = useState(false);

  // Load all resource IDs for this group
  const loadResourceIds = useCallback(async () => {
    const r = await BApi.comparison.getComparisonResultGroupResourceIds(planId, group.id!);
    const ids = r.data || [];
    setAllResourceIds(ids);

    // Get group details to find primary resource
    const groupDetail = await BApi.comparison.getComparisonResultGroup(planId, group.id!);
    const members = groupDetail.data?.members || [];
    const primary = members.find((m) => m.isSuggestedPrimary);
    if (primary) {
      setPrimaryResourceId(primary.resourceId);
    }
  }, [planId, group.id]);

  // Load resources for current page
  const loadResources = useCallback(async () => {
    if (allResourceIds.length === 0) return;

    setLoading(true);
    try {
      // Sort IDs to put primary first
      const sortedIds = [...allResourceIds].sort((a, b) => {
        if (a === primaryResourceId) return -1;
        if (b === primaryResourceId) return 1;
        return 0;
      });

      // Get IDs for current page
      const startIdx = (page - 1) * RESOURCES_PAGE_SIZE;
      const endIdx = Math.min(startIdx + RESOURCES_PAGE_SIZE, sortedIds.length);
      const pageIds = sortedIds.slice(startIdx, endIdx);

      if (pageIds.length === 0) {
        setResources([]);
        return;
      }

      // Fetch resources by IDs
      const r = await BApi.resource.getResourcesByKeys({
        ids: pageIds,
        additionalItems: ResourceAdditionalItem.All,
      });

      // Sort resources to match the order of pageIds (primary first)
      const resourceMap = new Map((r.data || []).map((res) => [res.id, res]));
      const sortedResources = pageIds
        .map((id) => resourceMap.get(id))
        .filter((res): res is ResourceModel => res !== undefined);

      setResources(sortedResources);
    } finally {
      setLoading(false);
    }
  }, [allResourceIds, page, primaryResourceId]);

  useEffect(() => {
    loadResourceIds();
  }, [loadResourceIds]);

  useEffect(() => {
    if (allResourceIds.length > 0) {
      loadResources();
    }
  }, [allResourceIds, loadResources]);

  const totalPages = Math.ceil(allResourceIds.length / RESOURCES_PAGE_SIZE);

  const handleHide = async () => {
    await BApi.comparison.hideComparisonResultGroup(planId, group.id!);
    onHiddenChange?.(true);
  };

  const handleUnhide = async () => {
    await BApi.comparison.unhideComparisonResultGroup(planId, group.id!);
    onHiddenChange?.(false);
  };

  // Use isHidden from API response, with fallback for type safety
  const isHidden = (group as any).isHidden ?? false;

  return (
    <Card className="bg-default-50">
      <CardHeader className="flex items-center justify-between pb-2">
        <div className="flex items-center gap-2">
          <span className="font-medium">
            {t("comparison.label.group")} #{group.id}
          </span>
          <Chip color="primary" size="sm" variant="flat">
            {group.memberCount} {t("comparison.label.resources")}
          </Chip>
        </div>
        <div className="flex items-center gap-1">
          <Tooltip content={t("comparison.action.viewNetworkGraph")}>
            <Button
              isIconOnly
              size="sm"
              variant="light"
              onPress={() => setShowNetworkGraph(true)}
            >
              <NodeIndexOutlined className="text-lg" />
            </Button>
          </Tooltip>
          {isHidden ? (
            <Tooltip content={t("comparison.action.unhideGroup")}>
              <Button
                isIconOnly
                size="sm"
                variant="light"
                onPress={handleUnhide}
              >
                <EyeOutlined className="text-lg" />
              </Button>
            </Tooltip>
          ) : (
            <Tooltip content={t("comparison.action.hideGroup")}>
              <Button
                isIconOnly
                size="sm"
                variant="light"
                onPress={handleHide}
              >
                <EyeInvisibleOutlined className="text-lg" />
              </Button>
            </Tooltip>
          )}
        </div>
      </CardHeader>
      <CardBody className="pt-0">
        {loading ? (
          <div className="flex items-center justify-center h-32">
            <Spinner size="sm" />
          </div>
        ) : resources.length === 0 ? (
          <div className="text-sm text-default-400 py-4">
            {t("comparison.empty.noResources")}
          </div>
        ) : (
          <div className="flex flex-col gap-3">
            <div className="grid grid-cols-10 gap-2">
              {resources.map((resource) => (
                <div
                  key={resource.id}
                  className="relative"
                >
                  {/* Primary indicator */}
                  {resource.id === primaryResourceId && (
                    <Tooltip content={t("comparison.label.primaryResource")}>
                      <div className="absolute top-2 left-2 z-10 bg-warning-500 rounded-full w-5 h-5 flex items-center justify-center shadow-md">
                        <StarFilled className="text-white text-[10px]" />
                      </div>
                    </Tooltip>
                  )}
                  <Resource
                    resource={resource}
                    disableCoverClick={false}
                  />
                </div>
              ))}
            </div>

            {/* Pagination within group */}
            {totalPages > 1 && (
              <div className="flex justify-center">
                <Pagination
                  size="sm"
                  page={page}
                  total={totalPages}
                  onChange={setPage}
                />
              </div>
            )}
          </div>
        )}
      </CardBody>

      {/* Network Graph Modal */}
      <ComparisonNetworkGraph
        planId={planId}
        groupId={group.id!}
        threshold={threshold}
        isOpen={showNetworkGraph}
        onClose={() => setShowNetworkGraph(false)}
      />
    </Card>
  );
};

export default ComparisonResultGroup;
