"use client";

import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router-dom";

import PathMarkTreeView from "./components/PathMarkTreeView";
import PathMarkSettingsButton from "./components/PathMarkSettingsButton";
import PendingSyncButton from "./components/PendingSyncButton";
import type { PendingSyncButtonRef } from "./components/PendingSyncButton";
import usePathMarks from "./hooks/usePathMarks";

import { useFileSystemOptionsStore } from "@/stores/options";
import { Chip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";

const PathRuleConfigPage = () => {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();

  const { loadAllMarks } = usePathMarks();

  const optionsStore = useFileSystemOptionsStore((state) => state);
  const [rootPath, setRootPath] = useState<string>();
  const [rootPathInitialized, setRootPathInitialized] = useState(false);

  const fpOptionsRef = useRef(optionsStore.data?.fileProcessor);
  const pendingSyncButtonRef = useRef<PendingSyncButtonRef>(null);

  // Refresh pending sync count
  const refreshPendingSyncCount = () => {
    pendingSyncButtonRef.current?.refresh();
  };

  useEffect(() => {
    if (!rootPathInitialized && optionsStore.initialized) {
      // Check URL params first
      const pathFromUrl = searchParams.get("path");

      if (pathFromUrl) {
        setRootPath(decodeURIComponent(pathFromUrl));
      } else {
        const p = optionsStore.data.fileProcessor?.workingDirectory;

        if (p) {
          setRootPath(p);
        }
      }
      setRootPathInitialized(true);
    }
  }, [rootPathInitialized, optionsStore.initialized, searchParams]);

  useEffect(() => {
    fpOptionsRef.current = optionsStore.data?.fileProcessor;
  }, [optionsStore]);

  const handleRootPathInitialized = (v: string | undefined) => {
    if (v != undefined) {
      BApi.options.patchFileSystemOptions({
        fileProcessor: {
          ...(fpOptionsRef.current ?? { showOperationsAfterPlayingFirstFile: false }),
          workingDirectory: v,
        },
      });
    }
  };

  return (
    <div className="path-mark-config-page h-full flex flex-col">
      <div className="flex flex-col gap-4 p-4 flex-1 min-h-0">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <h2 className="text-xl font-semibold">{t("Configure Your Media Library")}</h2>
            <Chip color="primary" size="sm" variant="flat">
              {t("Beta")}
            </Chip>
          </div>

          {/* Sync options */}
          <div className="flex items-center gap-2">
            {/* Pending sync button */}
            <PendingSyncButton ref={pendingSyncButtonRef} onSyncComplete={loadAllMarks} />

            {/* Settings button */}
            <PathMarkSettingsButton />
          </div>
        </div>

        <div className="text-sm text-default-500">{t("PathRuleConfig.Description")}</div>

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
      </div>
    </div>
  );
};

PathRuleConfigPage.displayName = "PathRuleConfigPage";

export default PathRuleConfigPage;
