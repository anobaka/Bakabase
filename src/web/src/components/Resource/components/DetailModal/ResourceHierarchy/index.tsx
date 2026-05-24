"use client";

import type { Resource } from "@/core/models/Resource";

import React, { useEffect, useState, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { TiFlowChildren } from "react-icons/ti";

import DetailModal from "../index";
import ChildrenModal from "../../ChildrenModal";

import { Breadcrumbs, BreadcrumbItem, Spinner } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";

type Ancestor = {
  id: number;
  displayName: string;
  parentId?: number;
};

type Props = {
  resource: Resource;
  onReload?: () => void;
};

const ResourceHierarchy: React.FC<Props> = ({ resource, onReload }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [ancestors, setAncestors] = useState<Ancestor[]>([]);
  const [loading, setLoading] = useState(false);
  const [childrenCount, setChildrenCount] = useState<number | undefined>(undefined);

  // Load hierarchy context (ancestors and children count) in a single API call
  const loadHierarchyContext = useCallback(async () => {
    // Only load if resource has parent or children
    if (!resource.parentId && !resource.hasChildren) {
      setAncestors([]);
      setChildrenCount(undefined);

      return;
    }

    setLoading(true);
    try {
      const res = await BApi.resource.getResourceHierarchyContext(resource.id);

      setAncestors(res.data?.ancestors ?? []);
      setChildrenCount(res.data?.childrenCount ?? undefined);
    } catch (error) {
      console.error("Failed to load hierarchy context:", error);
    } finally {
      setLoading(false);
    }
  }, [resource.id, resource.parentId, resource.hasChildren]);

  useEffect(() => {
    loadHierarchyContext();
  }, [loadHierarchyContext]);

  // Don't render if no parent and no children
  if (!resource.parentId && !resource.hasChildren) {
    return null;
  }

  const handleAncestorClick = (ancestorId: number) => {
    createPortal(DetailModal, {
      id: ancestorId,
      onDestroyed: onReload,
    });
  };

  const handleChildrenClick = () => {
    createPortal(ChildrenModal, {
      resourceId: resource.id,
      resourceDisplayName: resource.displayName,
    });
  };

  // Truncate ancestors if more than 3 levels
  const displayAncestors =
    ancestors.length > 3
      ? [ancestors[0], null, ...ancestors.slice(-2)] // null represents "..."
      : ancestors;

  return (
    <div className="flex flex-col gap-1">
      <div className="flex items-center gap-2">
        {loading ? (
          <Spinner size="sm" />
        ) : (
          <Breadcrumbs
            classNames={{
              list: "flex-wrap",
            }}
            size="sm"
            variant="light"
          >
            {displayAncestors.map((ancestor, index) => {
              if (ancestor === null) {
                // Ellipsis for truncated ancestors
                return (
                  <BreadcrumbItem key="ellipsis" isDisabled>
                    ...
                  </BreadcrumbItem>
                );
              }

              return (
                <BreadcrumbItem key={ancestor.id}>
                  <span
                    className="cursor-pointer hover:underline text-primary"
                    role="button"
                    tabIndex={0}
                    onClick={() => handleAncestorClick(ancestor.id)}
                    onKeyDown={(e) => {
                      if (e.key === "Enter" || e.key === " ") {
                        e.preventDefault();
                        handleAncestorClick(ancestor.id);
                      }
                    }}
                  >
                    {ancestor.displayName}
                  </span>
                </BreadcrumbItem>
              );
            })}

            {/* Current resource */}
            <BreadcrumbItem
              key={resource.id}
              isCurrent
              classNames={{
                item: "font-semibold",
              }}
            >
              {resource.displayName}
            </BreadcrumbItem>

            {/* Children */}
            {resource.hasChildren && (
              <BreadcrumbItem key="children">
                <span
                  className="flex items-center gap-1 cursor-pointer hover:underline text-primary"
                  role="button"
                  tabIndex={0}
                  onClick={handleChildrenClick}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" || e.key === " ") {
                      e.preventDefault();
                      handleChildrenClick();
                    }
                  }}
                >
                  <TiFlowChildren className="text-sm" />
                  {t("common.label.children")}
                  {childrenCount !== undefined && ` (${childrenCount})`}
                </span>
              </BreadcrumbItem>
            )}
          </Breadcrumbs>
        )}
      </div>

      {/* Description text */}
      <div className="text-xs text-default-500">{t("resource.tip.hierarchyDescription")}</div>
    </div>
  );
};

export default ResourceHierarchy;
