"use client";

import type { ResourceSearchFilterGroup } from "@/pages/resource/components/FilterPanel/FilterGroupsPanel/models";
import type {
  BulkModificationProcess,
  BulkModificationVariable,
} from "@/pages/bulk-modification2/components/BulkModification/models";

import { useCallback, useState } from "react";
import { useTranslation } from "react-i18next";
import { useTour } from "@reactour/tour";
import { QuestionCircleOutlined } from "@ant-design/icons";

import Variables from "./Variables";
import Processes from "./Processes";

import { Button, Chip, Divider, Modal, Tooltip } from "@/components/bakaui";
import FilterGroupsPanel from "@/pages/resource/components/FilterPanel/FilterGroupsPanel";
import BApi from "@/sdk/BApi";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import DiffsModal from "@/pages/bulk-modification2/components/BulkModification/DiffsModal";
import { buildLogger } from "@/components/utils";

export type BulkModification = {
  id: number;
  name: string;
  isActive: boolean;
  createdAt: string;
  variables?: BulkModificationVariable[];
  filter?: ResourceSearchFilterGroup;
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
const Blocks: BlockKey[] = [
  "Filters",
  "Variables",
  "Processes",
  "Changes",
  "FinalStep",
];

const log = buildLogger("BulkModification");

export default ({ bm, onChange }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const { isOpen, currentStep, steps, setIsOpen, setCurrentStep, setSteps } =
    useTour();

  const [calculatingDiffs, setCalculatingDiffs] = useState(false);

  const reload = useCallback(async () => {
    const r = await BApi.bulkModification.getBulkModification(bm.id);

    onChange(r.data!);
  }, [onChange]);

  log(bm);

  return (
    <div className={"flex items-start gap-2"}>
      <div className={"flex flex-col gap-2 grow"}>
        {Blocks.map((bk, i) => {
          let blockInner: any;
          let blockTip: any;

          switch (bk) {
            case "Filters":
              blockTip = (
                <div>{t<string>("Select resources to be modified.")}</div>
              );
              blockInner = (
                <div>
                  <div>
                    <FilterGroupsPanel
                      group={bm.filter}
                      onChange={(g) => {
                        bm.filter = g;
                        BApi.bulkModification.patchBulkModification(bm.id, {
                          filter: g,
                        });
                      }}
                    />
                  </div>
                  <div className={"flex items-center gap-2"}>
                    <Button
                      color={"primary"}
                      size={"sm"}
                      variant={"ghost"}
                      onClick={() => {
                        BApi.bulkModification
                          .filterResourcesInBulkModification(bm.id)
                          .then((r) => {
                            reload();
                          });
                      }}
                    >
                      {t<string>("Filter(Verb)")}
                    </Button>
                    <div className={""}>
                      {t<string>("{{count}} resources have been filtered out", {
                        count: bm.filteredResourceIds?.length || 0,
                      })}
                    </div>
                    {/* {bm.filteredAt && ( */}
                    {/* <div className={'filtered-at'}>{t<string>('Filtered at {{datetime}}', { datetime: bm.filteredAt })}</div> */}
                    {/* )} */}
                    {/* {bm.filteredResourceIds && bm.filteredResourceIds.length > 0 && ( */}
                    {/* <Button */}
                    {/*   color={'primary'} */}
                    {/*   variant={'light'} */}
                    {/*   size={'sm'} */}
                    {/*   onClick={() => { */}
                    {/*     FilteredResourcesDialog.show({ */}
                    {/*       bmId: bm.id!, */}
                    {/*     }); */}
                    {/*   }} */}
                    {/* >{t<string>('Check all the resources that have been filtered out')}</Button> */}
                    {/* )} */}
                  </div>
                </div>
              );
              break;
            case "Variables":
              blockTip = (
                <div>
                  {t<string>(
                    "You can define and preprocess variables here, then use it in process steps.",
                  )}
                </div>
              );
              blockInner = (
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
                      .then((r) => {
                        reload();
                      });
                  }}
                />
              );
              break;
            case "Processes":
              blockTip = (
                <div>
                  {t<string>(
                    "You can define process steps here, and use variables in steps, to modify all filtered resources.",
                  )}
                </div>
              );
              blockInner = (
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
                      .then((r) => {
                        reload();
                      });
                  }}
                />
              );
              break;
            case "Changes":
              blockTip = (
                <div>
                  {t<string>(
                    "Before applying bulk modification, you must calculate and check the changes here.",
                  )}
                </div>
              );
              blockInner = (
                <div className={"flex items-center gap-1"}>
                  <Button
                    color={"primary"}
                    isLoading={calculatingDiffs}
                    size={"sm"}
                    variant={"bordered"}
                    onClick={() => {
                      setCalculatingDiffs(true);
                      BApi.bulkModification
                        .previewBulkModification(bm.id)
                        .then((r) => {
                          reload();
                        })
                        .finally(() => {
                          setCalculatingDiffs(false);
                        });
                    }}
                  >
                    {t<string>("Calculate changes")}
                  </Button>
                  {bm.resourceDiffCount > 0 && (
                    <Button
                      color={"primary"}
                      size={"sm"}
                      variant={"light"}
                      onClick={() => {
                        createPortal(DiffsModal, {
                          bmId: bm.id,
                        });
                      }}
                    >
                      {t<string>("Check {{count}} diffs", {
                        count: bm.resourceDiffCount,
                      })}
                    </Button>
                  )}
                </div>
              );
              break;
            case "FinalStep":
              blockInner = (
                <div className={"flex items-center gap-1"}>
                  <Button
                    color={"primary"}
                    isDisabled={
                      !bm.processes ||
                      bm.processes.length === 0 ||
                      bm.resourceDiffCount == 0
                    }
                    size={"sm"}
                    onClick={() => {
                      createPortal(Modal, {
                        defaultVisible: true,
                        title: t<string>("Apply bulk modification"),
                        children: t<string>(
                          "Please check diffs before applying, and changed value may not be reverted perfectly in some situations,  are you sure to apply?",
                        ),
                        onOk: async () => {
                          await BApi.bulkModification.applyBulkModification(
                            bm.id,
                          );
                          reload();
                        },
                      });
                    }}
                  >
                    {t<string>("Apply changes")}
                  </Button>
                  {bm.appliedAt && (
                    <Button
                      color={"warning"}
                      size={"sm"}
                      variant={"flat"}
                      onClick={() => {
                        createPortal(Modal, {
                          defaultVisible: true,
                          title: t<string>("Revert bulk modification"),
                          children: t<string>(
                            "Are you sure to revert the bulk modification?",
                          ),
                          onOk: async () => {
                            await BApi.bulkModification.revertBulkModification(
                              bm.id,
                            );
                            reload();
                          },
                        });
                      }}
                    >
                      {t<string>("Revert changes")}
                    </Button>
                  )}
                </div>
              );
              break;
          }

          return (
            <>
              <div className={"flex gap-4 items-center"}>
                <div className={"w-[120px] text-right"}>
                  {blockTip ? (
                    <Tooltip content={blockTip}>
                      <Chip
                        classNames={{
                          content: "items-center flex gap-1",
                        }}
                        radius={"sm"}
                      >
                        {t<string>(bk)}
                        <QuestionCircleOutlined className={"text-base"} />
                      </Chip>
                    </Tooltip>
                  ) : (
                    <Chip radius={"sm"}>{t<string>(bk)}</Chip>
                  )}
                </div>
                {blockInner}
              </div>
              {i != Blocks.length - 1 && <Divider orientation={"horizontal"} />}
            </>
          );
        })}
      </div>
    </div>
  );
};
