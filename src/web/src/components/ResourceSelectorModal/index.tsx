"use client";

import type { DestroyableProps } from "@/components/bakaui/types";
import type { Resource as ResourceModel } from "@/core/models/Resource";
import type { SearchCriteria } from "@/components/ResourceFilter";

import React, { useEffect, useState, useCallback } from "react";
import { useTranslation } from "react-i18next";

import { Modal, Spinner, Pagination, Button, Chip } from "@/components/bakaui";
import Resource from "@/components/Resource";
import { buildLogger } from "@/components/utils";
import { useResourceSearch } from "@/hooks/useResourceSearch";
import { ResourceSearchPanel, toSearchInputModel } from "@/components/ResourceFilter";
import type { components } from "@/sdk/BApi2";

type SearchForm = components["schemas"]["Bakabase.Service.Models.Input.ResourceSearchInputModel"];

const log = buildLogger("ResourceSelectorModal");

export type ResourceSelectorValue = {
  id: number;
  displayName: string;
};

type ResourceSelectorModalProps = {
  /** Whether to allow multiple selection */
  multiple?: boolean;
  /** Default selected resource IDs */
  defaultSelectedIds?: number[];
  /** Default search criteria */
  defaultCriteria?: SearchCriteria;
  /** Callback when selection is confirmed */
  onConfirm?: (selectedResources: ResourceSelectorValue[]) => void;
  /** Callback when modal is cancelled */
  onCancel?: () => void;
  /** Modal title */
  title?: string;
} & DestroyableProps;

const PAGE_SIZE = 50;

const ResourceSelectorModal: React.FC<ResourceSelectorModalProps> = ({
  multiple = false,
  defaultSelectedIds = [],
  defaultCriteria,
  onConfirm,
  onCancel,
  onDestroyed,
  title,
}) => {
  const { t } = useTranslation();
  const { resources, loading, response, search } = useResourceSearch();
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const [selectedIds, setSelectedIds] = useState<number[]>(defaultSelectedIds);
  const [criteria, setCriteria] = useState<SearchCriteria>(
    defaultCriteria ?? {}
  );

  const doSearch = useCallback((pageNumber: number, searchCriteria: SearchCriteria) => {
    setError(null);
    const inputModel = toSearchInputModel({
      ...searchCriteria,
      page: pageNumber,
      pageSize: PAGE_SIZE,
    });
    search(inputModel as SearchForm, { saveSearch: false })
      .then((result) => {
        log("Found resources:", result.length);
      })
      .catch((err) => {
        console.error("Failed to search resources:", err);
        setError(t<string>("Failed to load resources"));
      });
  }, [search, t]);

  useEffect(() => {
    doSearch(1, criteria);
  }, []);

  const handlePageChange = (newPage: number) => {
    setPage(newPage);
    doSearch(newPage, criteria);
  };

  const handleCriteriaChange = (newCriteria: SearchCriteria) => {
    setCriteria(newCriteria);
    setPage(1);
    doSearch(1, newCriteria);
  };

  const handleResourceClick = (resource: ResourceModel) => {
    if (multiple) {
      setSelectedIds((prev) => {
        if (prev.includes(resource.id)) {
          return prev.filter((id) => id !== resource.id);
        }
        return [...prev, resource.id];
      });
    } else {
      setSelectedIds([resource.id]);
    }
  };

  const handleConfirm = () => {
    const selectedResources: ResourceSelectorValue[] = selectedIds
      .map((id) => {
        const resource = resources.find((r) => r.id === id);
        if (resource) {
          return {
            id: resource.id,
            displayName: resource.displayName || resource.fileName || `Resource ${resource.id}`,
          };
        }
        // For resources not in current page, use ID as displayName
        return {
          id,
          displayName: `Resource ${id}`,
        };
      });
    onConfirm?.(selectedResources);
    onDestroyed?.();
  };

  const handleCancel = () => {
    onCancel?.();
    onDestroyed?.();
  };

  const totalPages = response ? Math.ceil(response.totalCount / PAGE_SIZE) : 0;

  return (
    <Modal
      defaultVisible
      size="7xl"
      title={title || t<string>(multiple ? "Select Resources" : "Select Resource")}
      onDestroyed={onDestroyed}
      footer={(
        <div className="flex justify-between items-center w-full">
          <div className="flex items-center gap-2">
            {selectedIds.length > 0 && (
              <Chip color="primary" size="sm">
                {t<string>("{{count}} selected", { count: selectedIds.length })}
              </Chip>
            )}
          </div>
          <div className="flex gap-2">
            <Button
              color="default"
              variant="light"
              onPress={handleCancel}
            >
              {t<string>("Cancel")}
            </Button>
            <Button
              color="primary"
              isDisabled={selectedIds.length === 0}
              onPress={handleConfirm}
            >
              {t<string>("Confirm")}
            </Button>
          </div>
        </div>
      )}
    >
      <div className="flex flex-col gap-4 min-h-[400px]">
        {/* Filter Section */}
        <div className="border-b pb-4">
          <ResourceSearchPanel
            compact
            criteria={criteria}
            showKeyword={false}
            showRecentFilters={false}
            showTags
            onChange={handleCriteriaChange}
          />
        </div>

        {/* Resources Grid */}
        <div className="flex-1">
          {loading ? (
            <div className="flex justify-center items-center h-32">
              <Spinner size="lg" />
              <span className="ml-2">{t<string>("Loading resources...")}</span>
            </div>
          ) : error ? (
            <div className="text-center text-red-500 p-4">{error}</div>
          ) : resources.length === 0 ? (
            <div className="text-center text-gray-500 p-4">
              {t<string>("No resources found")}
            </div>
          ) : (
            <div className="flex flex-col gap-4">
              <div className="text-sm text-gray-500">
                {t<string>("Found {{count}} resources", {
                  count: response?.totalCount ?? resources.length,
                })}
              </div>
              <div className="grid grid-cols-8 gap-1">
                {resources.map((resource) => (
                  <div
                    key={resource.id}
                    className={`cursor-pointer transition-all rounded-lg ${
                      selectedIds.includes(resource.id)
                        ? "ring-2 ring-primary ring-offset-2"
                        : "hover:ring-2 hover:ring-gray-300 hover:ring-offset-2"
                    }`}
                    onClick={() => handleResourceClick(resource)}
                  >
                    <Resource
                      disableCoverClick
                      resource={resource}
                      selected={selectedIds.includes(resource.id)}
                      selectedResourceIds={selectedIds}
                      onSelected={() => handleResourceClick(resource)}
                      onSelectedResourcesChanged={() => {}}
                    />
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>

        {/* Pagination */}
        {totalPages > 1 && (
          <div className="flex justify-center pt-4 border-t">
            <Pagination
              showControls
              page={page}
              total={totalPages}
              onChange={handlePageChange}
            />
          </div>
        )}
      </div>
    </Modal>
  );
};

export default ResourceSelectorModal;
