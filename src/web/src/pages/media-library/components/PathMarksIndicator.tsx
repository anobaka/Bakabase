"use client";

import type { BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { RiRulerLine } from "react-icons/ri";
import { useNavigate } from "react-router-dom";
import BApi from "@/sdk/BApi";
import {
  Button,
  Chip,
  Popover,
  Spinner,
} from "@/components/bakaui";
import { PathMarkType } from "@/sdk/constants";

// Group marks by path
interface PathGroup {
  path: string;
  marks: BakabaseAbstractionsModelsDomainPathMark[];
}

interface Props {
  mediaLibraryId: number;
  rootPaths?: string[];
}

const PathMarksIndicator: React.FC<Props> = ({ mediaLibraryId, rootPaths }) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [pathGroups, setPathGroups] = useState<PathGroup[]>([]);
  const [isOpen, setIsOpen] = useState(false);

  const loadPathMarks = useCallback(async () => {
    setLoading(true);
    try {
      const rsp = await BApi.pathMark.getAllPathMarks();
      const allMarks = rsp.data || [];

      // Filter marks to those under the media library's root paths
      let relevantMarks = allMarks;
      if (rootPaths && rootPaths.length > 0) {
        relevantMarks = allMarks.filter((mark) => {
          if (!mark.path) return false;
          const normalizedMarkPath = mark.path.replace(/\\/g, "/").toLowerCase();
          return rootPaths.some((rootPath) => {
            const normalizedRootPath = rootPath.replace(/\\/g, "/").toLowerCase();
            return normalizedMarkPath.startsWith(normalizedRootPath);
          });
        });
      }

      // Group by path
      const groups: Map<string, BakabaseAbstractionsModelsDomainPathMark[]> = new Map();
      for (const mark of relevantMarks) {
        const path = mark.path || "Unknown";
        if (!groups.has(path)) {
          groups.set(path, []);
        }
        groups.get(path)!.push(mark);
      }

      setPathGroups(
        Array.from(groups.entries()).map(([path, marks]) => ({
          path,
          marks: marks.sort((a, b) => (a.priority || 0) - (b.priority || 0)),
        }))
      );
    } catch (error) {
      console.error("Failed to load path marks:", error);
    } finally {
      setLoading(false);
    }
  }, [rootPaths]);

  useEffect(() => {
    if (isOpen) {
      loadPathMarks();
    }
  }, [isOpen, loadPathMarks]);

  const handleNavigateToPathConfig = (path: string) => {
    navigate(`/path-mark-config?path=${encodeURIComponent(path)}`);
  };

  const totalMarks = pathGroups.reduce((sum, group) => sum + group.marks.length, 0);

  return (
    <Popover
      visible={isOpen}
      onVisibleChange={setIsOpen}
      placement="bottom-start"
      trigger={
        <Button
          isIconOnly
          size="sm"
          variant="light"
          className="opacity-70 hover:opacity-100"
        >
          <RiRulerLine className="text-lg" />
        </Button>
      }
    >
      <div className="p-2 min-w-[250px] max-w-[400px]">
        <div className="flex items-center justify-between mb-2">
          <span className="font-medium text-sm">{t("Path Marks")}</span>
          <Button
            size="sm"
            variant="light"
            color="primary"
            onPress={() => navigate("/path-mark-config")}
          >
            {t("Manage")}
          </Button>
        </div>

        {loading ? (
          <div className="flex items-center justify-center py-4">
            <Spinner size="sm" />
          </div>
        ) : pathGroups.length === 0 ? (
          <div className="text-sm text-default-400 py-2 text-center">
            {t("No path marks configured")}
          </div>
        ) : (
          <div className="flex flex-col gap-1 max-h-60 overflow-y-auto">
            {pathGroups.map((group) => {
              const resourceMarks = group.marks.filter((m) => m.type === PathMarkType.Resource);
              const propertyMarks = group.marks.filter((m) => m.type === PathMarkType.Property);

              return (
                <div
                  key={group.path}
                  className="flex items-center gap-2 p-2 rounded hover:bg-default-100 cursor-pointer"
                  onClick={() => handleNavigateToPathConfig(group.path)}
                >
                  <div className="flex-1 min-w-0">
                    <div className="text-sm font-medium truncate" title={group.path}>
                      {group.path}
                    </div>
                    <div className="flex items-center gap-1 mt-1">
                      {resourceMarks.length > 0 && (
                        <Chip size="sm" variant="flat" color="success">
                          {resourceMarks.length} {t("Resource")}
                        </Chip>
                      )}
                      {propertyMarks.length > 0 && (
                        <Chip size="sm" variant="flat" color="primary">
                          {propertyMarks.length} {t("Property")}
                        </Chip>
                      )}
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>
    </Popover>
  );
};

PathMarksIndicator.displayName = "PathMarksIndicator";

export default PathMarksIndicator;
