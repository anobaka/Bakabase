"use client";

import type {
  BulkModificationProcess,
  BulkModificationVariable,
} from "@/pages/bulk-modification/components/BulkModification/models";
import type { ReactNode } from "react";

import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineArrowDown } from "react-icons/ai";

import Variables from "./Variables";
import Processes from "./Processes";

import { Button, Modal } from "@/components/bakaui";
import { ResourceFilterController } from "@/components/ResourceFilter";
import type { SearchCriteria } from "@/components/ResourceFilter";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import DiffsModal from "@/pages/bulk-modification/components/BulkModification/DiffsModal";
import ResourcesModal from "@/components/ResourcesModal";
import { FilterDisplayMode } from "@/sdk/constants";

export type BulkModification = {
  id: number;
  name: string;
  isActive: boolean;
  createdAt: string;
  variables?: BulkModificationVariable[];
  search?: SearchCriteria;
  processes?: BulkModificationProcess[];
  filteredResourceIds?: number[];
  appliedAt?: string;
  resourceDiffCount: number;
};

type Props = {
  bm: BulkModification;
  onChange: (bm: BulkModification) => any;
};

type BlockKey = "Filters" | "Variables" | "Processes" | "Changes" | "FinalStep";

interface BlockConfig {
  key: BlockKey;
  i18nKey: string;
  tipKey?: string;
  stepNumber: number;
}

const BlockConfigs: BlockConfig[] = [
  { key: "Filters", i18nKey: "bulkModification.block.filters", tipKey: "bulkModification.tip.filters", stepNumber: 1 },
  { key: "Variables", i18nKey: "bulkModification.block.variables", tipKey: "bulkModification.tip.variables", stepNumber: 2 },
  { key: "Processes", i18nKey: "bulkModification.block.processes", tipKey: "bulkModification.tip.processes", stepNumber: 3 },
  { key: "Changes", i18nKey: "bulkModification.block.changes", tipKey: "bulkModification.tip.changes", stepNumber: 4 },
  { key: "FinalStep", i18nKey: "bulkModification.block.finalStep", stepNumber: 5 },
];

const BulkModification = ({ bm, onChange }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [calculatingDiffs, setCalculatingDiffs] = useState(false);
  const [filtering, setFiltering] = useState(false);

  const reload = useCallback(async () => {
    const r = await BApi.bulkModification.getBulkModification(bm.id);
    onChange(r.data!);
  }, [onChange, bm.id]);

  const renderBlockContent = (blockKey: BlockKey): ReactNode => {
    switch (blockKey) {
      case "Filters":
        return (
          <div className="flex flex-col gap-3">
            <ResourceFilterController
              filterDisplayMode={FilterDisplayMode.Simple}
              autoCreateMediaLibraryFilter={true}
              group={bm.search?.group}
              onGroupChange={(group) => {
                const updatedSearch = { ...bm.search, group };
                // 更新父组件状态（立即更新 UI）
                onChange({ ...bm, search: updatedSearch });
                // 保存到服务器（不 reload，避免服务器返回的不完整数据覆盖本地状态）
                BApi.bulkModification.patchBulkModification(bm.id, {
                  search: {
                    group: group,
                    tags: bm.search?.tags,
                    page: 1,
                    pageSize: 0,
                  },
                });
              }}
              tags={bm.search?.tags}
              onTagsChange={(tags) => {
                const updatedSearch = { ...bm.search, tags };
                // 更新父组件状态（立即更新 UI）
                onChange({ ...bm, search: updatedSearch });
                // 保存到服务器
                BApi.bulkModification.patchBulkModification(bm.id, {
                  search: {
                    group: bm.search?.group,
                    tags: tags,
                    page: 1,
                    pageSize: 0,
                  },
                });
              }}
              filterLayout="vertical"
              showRecentFilters
              showTags
            />
            <div className="flex items-center gap-2">
              <Button
                color="primary"
                isLoading={filtering}
                size="sm"
                variant="ghost"
                onPress={() => {
                  setFiltering(true);
                  BApi.bulkModification.filterResourcesInBulkModification(bm.id).then(() => {
                    reload();
                  }).finally(() => {
                    setFiltering(false);
                  });
                }}
              >
                {t<string>(bm.filteredResourceIds && bm.filteredResourceIds.length > 0
                  ? "bulkModification.action.refilter"
                  : "bulkModification.action.filter")}
              </Button>
              {bm.filteredResourceIds && bm.filteredResourceIds.length > 0 && (
                <Button
                  size="sm"
                  variant="flat"
                  color="success"
                  onPress={() => {
                    createPortal(ResourcesModal, {
                      resourceIds: bm.filteredResourceIds!,
                    });
                  }}
                >
                  {t<string>("bulkModification.action.viewFilteredResources", {
                    count: bm.filteredResourceIds.length,
                  })}
                </Button>
              )}
            </div>
          </div>
        );

      case "Variables":
        return (
          <Variables
            variables={bm.variables}
            onChange={(vs) => {
              bm.variables = vs;
              BApi.bulkModification
                .patchBulkModification(bm.id, {
                  variables: vs.map((v) => ({
                    ...v,
                    preprocesses: JSON.stringify(v.preprocesses),
                  })),
                })
                .then(() => {
                  reload();
                });
            }}
          />
        );

      case "Processes":
        return (
          <Processes
            processes={bm.processes}
            variables={bm.variables}
            onChange={(ps) => {
              bm.processes = ps;
              BApi.bulkModification
                .patchBulkModification(bm.id, {
                  processes: ps.map((v) => ({
                    ...v,
                    steps: JSON.stringify(v.steps),
                  })),
                })
                .then(() => {
                  reload();
                });
            }}
          />
        );

      case "Changes":
        return (
          <div className="flex items-center gap-2">
            <Button
              color="primary"
              isLoading={calculatingDiffs}
              size="sm"
              variant="bordered"
              onPress={() => {
                setCalculatingDiffs(true);
                BApi.bulkModification
                  .previewBulkModification(bm.id)
                  .then(() => {
                    reload();
                  })
                  .finally(() => {
                    setCalculatingDiffs(false);
                  });
              }}
            >
              {t<string>("bulkModification.action.calculate")}
            </Button>
            {bm.resourceDiffCount > 0 && (
              <Button
                color="primary"
                size="sm"
                variant="light"
                onPress={() => {
                  createPortal(DiffsModal, {
                    bmId: bm.id,
                  });
                }}
              >
                {t<string>("bulkModification.action.checkDiffs", {
                  count: bm.resourceDiffCount,
                })}
              </Button>
            )}
          </div>
        );

      case "FinalStep":
        return (
          <div className="flex items-center gap-2">
            <Button
              color="primary"
              isDisabled={!bm.processes || bm.processes.length === 0 || bm.resourceDiffCount === 0}
              size="sm"
              onPress={() => {
                createPortal(Modal, {
                  defaultVisible: true,
                  title: t<string>("bulkModification.action.apply"),
                  children: t<string>("bulkModification.confirm.apply"),
                  onOk: async () => {
                    await BApi.bulkModification.applyBulkModification(bm.id);
                    reload();
                  },
                });
              }}
            >
              {t<string>("bulkModification.action.applyChanges")}
            </Button>
            {bm.appliedAt && (
              <Button
                color="warning"
                size="sm"
                variant="flat"
                onPress={() => {
                  createPortal(Modal, {
                    defaultVisible: true,
                    title: t<string>("bulkModification.action.revert"),
                    children: t<string>("bulkModification.confirm.revert"),
                    onOk: async () => {
                      await BApi.bulkModification.revertBulkModification(bm.id);
                      reload();
                    },
                  });
                }}
              >
                {t<string>("bulkModification.action.revertChanges")}
              </Button>
            )}
          </div>
        );

      default:
        return null;
    }
  };

  return (
    <div className="flex flex-col">
      {BlockConfigs.map((config, i) => (
        <div
          key={config.key}
          className="flex items-stretch gap-4 p-3 rounded-lg bg-default-50 hover:bg-default-100 transition-colors"
        >
          {/* Step indicator column */}
          <div className="flex flex-col items-center justify-between">
            {/* Step number at top */}
            <div className="w-7 h-7 rounded-full bg-primary/10 text-primary flex items-center justify-center text-sm font-medium">
              {config.stepNumber}
            </div>
            {/* Arrow at bottom */}
            {i < BlockConfigs.length - 1 && (
              <AiOutlineArrowDown className="text-default-300 text-lg mt-2" />
            )}
          </div>

          {/* Content */}
          <div className="flex-1 min-w-0">
            {/* Header with description */}
            <div className="mb-2">
              <span className="font-medium text-sm">{t<string>(config.i18nKey)}</span>
              {config.tipKey && (
                <p className="text-xs text-default-400 mt-0.5">{t<string>(config.tipKey)}</p>
              )}
            </div>

            {/* Block content */}
            <div className="text-sm">
              {renderBlockContent(config.key)}
            </div>
          </div>
        </div>
      ))}
    </div>
  );
};

BulkModification.displayName = "BulkModification";

export default BulkModification;
