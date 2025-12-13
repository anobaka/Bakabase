"use client";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { RiRulerLine } from "react-icons/ri";
import { useNavigate } from "react-router-dom";
import BApi from "@/sdk/BApi";
import type { BakabaseAbstractionsModelsDomainPathRule } from "@/sdk/Api";
import {
  Button,
  Chip,
  Popover,
  Spinner,
  Tooltip,
} from "@/components/bakaui";

interface Props {
  mediaLibraryId: number;
}

const PathRulesIndicator: React.FC<Props> = ({ mediaLibraryId }) => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [pathRules, setPathRules] = useState<BakabaseAbstractionsModelsDomainPathRule[]>([]);
  const [isOpen, setIsOpen] = useState(false);

  const loadPathRules = useCallback(async () => {
    setLoading(true);
    try {
      const rsp = await BApi.mediaLibraryV2.getMediaLibraryV2PathRules(mediaLibraryId);
      setPathRules(rsp.data || []);
    } catch (error) {
      console.error("Failed to load path rules:", error);
    } finally {
      setLoading(false);
    }
  }, [mediaLibraryId]);

  useEffect(() => {
    if (isOpen) {
      loadPathRules();
    }
  }, [isOpen, loadPathRules]);

  const handleNavigateToPathRule = (ruleId: number) => {
    navigate(`/path-rule?highlight=${ruleId}`);
  };

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
          <span className="font-medium text-sm">{t("Associated Path Rules")}</span>
          <Button
            size="sm"
            variant="light"
            color="primary"
            onPress={() => navigate("/path-rule")}
          >
            {t("Manage")}
          </Button>
        </div>

        {loading ? (
          <div className="flex items-center justify-center py-4">
            <Spinner size="sm" />
          </div>
        ) : pathRules.length === 0 ? (
          <div className="text-sm text-default-400 py-2 text-center">
            {t("No path rules associated with this media library")}
          </div>
        ) : (
          <div className="flex flex-col gap-1 max-h-60 overflow-y-auto">
            {pathRules.map((rule) => (
              <div
                key={rule.id}
                className="flex items-center gap-2 p-2 rounded hover:bg-default-100 cursor-pointer"
                onClick={() => handleNavigateToPathRule(rule.id)}
              >
                <div className="flex-1 min-w-0">
                  <div className="text-sm font-medium truncate" title={rule.path}>
                    {rule.path}
                  </div>
                  <div className="flex items-center gap-1 mt-1">
                    <Chip size="sm" variant="flat" color="secondary">
                      {rule.marks.length} {t("mark(s)")}
                    </Chip>
                  </div>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </Popover>
  );
};

PathRulesIndicator.displayName = "PathRulesIndicator";

export default PathRulesIndicator;
