"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate, useSearchParams } from "react-router-dom";
import { AiOutlineTags } from "react-icons/ai";

import PathMarkTreeView from "./components/PathMarkTreeView";
import PathMarkSettingsButton from "./components/PathMarkSettingsButton";
import PendingSyncButton from "./components/PendingSyncButton";
import type { PendingSyncButtonRef } from "./components/PendingSyncButton";
import usePathMarks from "./hooks/usePathMarks";
import { PathMarkGuideModal, usePathMarkGuide } from "./components/PathMarkGuide";

import { Button, Chip } from "@/components/bakaui";

const PATH_MARK_ROOT_PATH_KEY = "pathMarkConfig.rootPath";

const PathRuleConfigPage = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const { loadAllMarks } = usePathMarks();
  const { showGuide, completeGuide } = usePathMarkGuide();

  const [rootPath, setRootPath] = useState<string>();
  const [rootPathInitialized, setRootPathInitialized] = useState(false);

  const pendingSyncButtonRef = useRef<PendingSyncButtonRef>(null);

  // Refresh pending sync count
  const refreshPendingSyncCount = () => {
    pendingSyncButtonRef.current?.refresh();
  };

  useEffect(() => {
    if (!rootPathInitialized) {
      // Check URL params first
      const pathFromUrl = searchParams.get("path");

      if (pathFromUrl) {
        setRootPath(decodeURIComponent(pathFromUrl));
      } else {
        // Read from localStorage
        const savedPath = localStorage.getItem(PATH_MARK_ROOT_PATH_KEY);

        if (savedPath) {
          setRootPath(savedPath);
        }
      }
      setRootPathInitialized(true);
    }
  }, [rootPathInitialized, searchParams]);

  const handleRootPathInitialized = (v: string | undefined) => {
    if (v != undefined) {
      localStorage.setItem(PATH_MARK_ROOT_PATH_KEY, v);
    }
  };

  return (
    <div className="path-mark-config-page h-full flex flex-col">
      <div className="flex flex-col gap-2 p-2 flex-1 min-h-0">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <h2 className="text-xl font-semibold">{t("pathMarkConfig.title")}</h2>
            <Chip color="primary" size="sm" variant="flat">
              {t("pathMarkConfig.label.beta")}
            </Chip>
          </div>

          {/* Actions */}
          <div className="flex items-center gap-2">
            {/* View all marks button */}
            <Button
              size="sm"
              startContent={<AiOutlineTags />}
              variant="flat"
              onPress={() => navigate("/path-marks")}
            >
              {t("pathMarkConfig.action.viewAllMarks")}
            </Button>

            {/* Pending sync button */}
            <PendingSyncButton ref={pendingSyncButtonRef} onSyncComplete={loadAllMarks} />

            {/* Settings button */}
            <PathMarkSettingsButton />
          </div>
        </div>

        <div className="text-sm text-default-500">{t("pathMarkConfig.tip.description")}</div>
        <div className="text-sm text-default-400">{t("pathMarkConfig.tip.pathMemory")}</div>

        <div className="overflow-hidden flex-1 min-h-0 flex">
          {/* Tree container with sidebar */}
          <div className="flex-1 min-w-0 overflow-hidden h-full">
            {rootPathInitialized && (
              <PathMarkTreeView
                rootPath={rootPath}
                onMarksChanged={refreshPendingSyncCount}
                onInitialized={handleRootPathInitialized}
              />
            )}
          </div>
        </div>

        {/* Next step hint */}
        <div className="flex items-center justify-center gap-2 p-3 bg-default-50 rounded-lg">
          <span className="text-sm text-default-500">{t("pathMarkConfig.label.nextStepHint")}</span>
          <Button
            color="secondary"
            size="sm"
            variant="flat"
            onPress={() => navigate("/path-marks")}
          >
            {t("pathMarkConfig.action.viewAllMarks")}
          </Button>
          <Button
            color="secondary"
            size="sm"
            variant="flat"
            onPress={() => navigate("/resource-profile")}
          >
            {t("pathMarkConfig.action.goToResourceProfile")}
          </Button>
        </div>
      </div>

      {/* First-time user guide */}
      <PathMarkGuideModal visible={showGuide} onComplete={completeGuide} />
    </div>
  );
};

PathRuleConfigPage.displayName = "PathRuleConfigPage";

export default PathRuleConfigPage;
