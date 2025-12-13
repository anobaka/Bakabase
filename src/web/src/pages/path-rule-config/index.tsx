"use client";

import { useEffect, useRef, useState, useCallback } from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router-dom";

import type { Entry } from "@/core/models/FileExplorer/Entry";
import type { BakabaseAbstractionsModelsDomainPathRule, BakabaseAbstractionsModelsDomainPathMark } from "@/sdk/Api";

import { useFileSystemOptionsStore } from "@/stores/options";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { Chip, toast, Modal } from "@/components/bakaui";
import RootTreeEntry from "@/pages/file-processor/RootTreeEntry";
import PathRuleConfigPanel from "./components/PathRuleConfigPanel";
import PathRuleMarks from "./components/PathRuleMarks";
import usePathRules from "./hooks/usePathRules";
import BApi from "@/sdk/BApi";

const PathRuleConfigPage = () => {
  const { t } = useTranslation();
  const [searchParams] = useSearchParams();

  const [selectedEntries, setSelectedEntries] = useState<Entry[]>([]);
  const { allRules: pathRules, loading: pathRulesLoading, loadAllRules, getRuleForPath } = usePathRules();

  const optionsStore = useFileSystemOptionsStore((state) => state);
  const [rootPath, setRootPath] = useState<string>();
  const [rootPathInitialized, setRootPathInitialized] = useState(false);

  const fpOptionsRef = useRef(optionsStore.data?.fileProcessor);

  const { createPortal } = useBakabaseContext();

  useEffect(() => {
    if (!rootPathInitialized && optionsStore.initialized) {
      // Check URL params first
      const pathFromUrl = searchParams.get('path');

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

  const handleOpenAdvancedConfig = useCallback((entry: Entry) => {
    createPortal(PathRuleConfigPanel, {
      selectedEntries: [entry],
      onComplete: () => {
        loadAllRules();
      },
    });
  }, [createPortal, loadAllRules]);

  const handleSaveMark = useCallback(async (
    entry: Entry,
    pathRule: BakabaseAbstractionsModelsDomainPathRule | undefined,
    newMark: BakabaseAbstractionsModelsDomainPathMark,
    oldMark?: BakabaseAbstractionsModelsDomainPathMark
  ) => {
    try {
      let updatedMarks: BakabaseAbstractionsModelsDomainPathMark[];

      if (oldMark) {
        // Edit existing mark
        updatedMarks = (pathRule?.marks || []).map(m => m === oldMark ? newMark : m);
      } else {
        // Add new mark
        updatedMarks = [...(pathRule?.marks || []), newMark];
      }

      if (pathRule?.id) {
        // Update existing path rule
        // @ts-ignore - API will be available after SDK regeneration
        await BApi.pathRule.updatePathRuleMarks({
          pathRuleId: pathRule.id,
          marksJson: JSON.stringify(updatedMarks),
        });
      } else {
        // Create new path rule for this entry
        // @ts-ignore - API will be available after SDK regeneration
        await BApi.pathRule.createPathRule({
          path: entry.path,
          marks: updatedMarks,
        });
      }

      toast.success(t(oldMark ? "Mark updated successfully" : "Mark added successfully"));
      loadAllRules();
    } catch (error) {
      console.error("Failed to save mark", error);
      toast.danger(t("Failed to save mark"));
    }
  }, [t, loadAllRules]);

  const handleDeleteMark = useCallback(async (pathRule: BakabaseAbstractionsModelsDomainPathRule, mark: BakabaseAbstractionsModelsDomainPathMark) => {
    const confirmed = await new Promise<boolean>((resolve) => {
      const modal = createPortal(Modal, {
        defaultVisible: true,
        title: t("Confirm Delete Mark"),
        children: (
          <div>
            <p>{t("Are you sure you want to delete this mark?")}</p>
            <p className="text-sm text-default-500 mt-2">
              {t("Path")}: {pathRule.path}
            </p>
            <p className="text-sm text-default-500">
              {t("Priority")}: {mark.priority}
            </p>
          </div>
        ),
        footer: {
          actions: ["cancel", "ok"],
          okProps: {
            color: "danger",
            children: t("Delete"),
          },
        },
        onOk: () => {
          resolve(true);
          modal.destroy();
        },
        onDestroyed: () => {
          resolve(false);
        },
      });
    });

    if (confirmed && pathRule.id) {
      try {
        // Remove the mark from the path rule
        const updatedMarks = (pathRule.marks || []).filter(m => m !== mark);

        // @ts-ignore - API will be available after SDK regeneration
        await BApi.pathRule.updatePathRuleMarks({
          pathRuleId: pathRule.id,
          marksJson: JSON.stringify(updatedMarks),
        });

        toast.success(t("Mark deleted successfully"));
        loadAllRules();
      } catch (error) {
        console.error("Failed to delete mark", error);
        toast.danger(t("Failed to delete mark"));
      }
    }
  }, [createPortal, t, loadAllRules]);

  // Render function to display path rule marks after entry name
  const renderAfterName = useCallback((entry: Entry) => {
    const pathRule = getRuleForPath(entry.path);

    return (
      <PathRuleMarks
        entry={entry}
        pathRule={pathRule ?? undefined}
        onSaveMark={handleSaveMark}
        onDeleteMark={handleDeleteMark}
        onOpenAdvancedConfig={handleOpenAdvancedConfig}
      />
    );
  }, [getRuleForPath, handleSaveMark, handleDeleteMark, handleOpenAdvancedConfig]);

  return (
    <div className="path-rule-config-page h-full flex flex-col">
      <div className="flex flex-col gap-4 p-4 flex-1 min-h-0">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-2">
            <h2 className="text-xl font-semibold">{t("Path Rule Configuration")}</h2>
            <Chip size="sm" variant="flat" color="primary">
              {t("Beta")}
            </Chip>
          </div>
        </div>

        <div className="flex flex-col gap-2">
          <div className="text-sm text-default-500">
            {t(
              "Browse and select folders or files below, then click 'Configure Path Rules' to set up path rules for the selected items."
            )}
          </div>
        </div>

        <div className="overflow-hidden flex-1 min-h-0 flex">
          {rootPathInitialized && (
            <RootTreeEntry
              expandable
              capabilities={["select", "multi-select", "range-select", "configure-path-rule"]}
              rootPath={rootPath}
              selectable="multiple"
              onInitialized={(v) => {
                if (v != undefined) {
                  BApi.options.patchFileSystemOptions({
                    fileProcessor: {
                      ...(fpOptionsRef.current ?? { showOperationsAfterPlayingFirstFile: false }),
                      workingDirectory: v,
                    },
                  });
                }
              }}
              onSelected={(entries) => {
                setSelectedEntries(entries);
              }}
              renderAfterName={renderAfterName}
            />
          )}
        </div>
      </div>
    </div>
  );
};

PathRuleConfigPage.displayName = "PathRuleConfigPage";

export default PathRuleConfigPage;
