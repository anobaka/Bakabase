"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Accordion,
  AccordionItem,
  Button,
  Chip,
  Table,
  TableBody,
  TableCell,
  TableColumn,
  TableHeader,
  TableRow,
} from "@heroui/react";
import { AiOutlineDelete, AiOutlineStop } from "react-icons/ai";

import { Select, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ConfirmModal from "@/components/ConfirmModal";
import { selectTasks, useBTasksStore } from "@/stores/bTasks";
import BApi from "@/sdk/BApi";
import type {
  BakabaseModulesAIModelsDbAigcArtifactDbModel,
  BakabaseModulesAIModelsDbAigcGenerationRunDbModel,
  BakabaseModulesAIModelsDbAigcGeneratorDbModel,
  BakabaseModulesAIModelsDomainAigcGeneratorView,
} from "@/sdk/Api";

type Run = BakabaseModulesAIModelsDbAigcGenerationRunDbModel;
type Artifact = BakabaseModulesAIModelsDbAigcArtifactDbModel;
type Generator = BakabaseModulesAIModelsDbAigcGeneratorDbModel;

const STATUS_COLORS: Record<number, "default" | "primary" | "success" | "danger" | "warning"> = {
  1: "default",   // Pending
  2: "primary",   // Running
  3: "success",   // Succeeded
  4: "danger",    // Failed
  5: "warning",   // Imported
  6: "default",   // Cancelled
};

const RUN_STATUS_PENDING = 1;
const RUN_STATUS_RUNNING = 2;
const isStoppable = (status: number) =>
  status === RUN_STATUS_PENDING || status === RUN_STATUS_RUNNING;

const AIGC_RUN_BTASK_PREFIX = "AigcGenerationRun:";

const AigcRecordsPage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [runs, setRuns] = useState<Run[]>([]);
  const [artifactsByRun, setArtifactsByRun] = useState<Record<number, Artifact[]>>({});
  const [generators, setGenerators] = useState<Generator[]>([]);
  const [filterGeneratorId, setFilterGeneratorId] = useState<number | null>(null);

  const load = useCallback(async () => {
    const [rr, gr] = await Promise.all([
      BApi.aigc.getAigcRuns({ generatorId: filterGeneratorId ?? undefined } as any),
      BApi.aigc.getAllAigcGenerators(),
    ]);
    if (!rr.code && rr.data) setRuns(rr.data);
    if (!gr.code && gr.data) {
      setGenerators(gr.data.map((v: BakabaseModulesAIModelsDomainAigcGeneratorView) => v.generator));
    }
  }, [filterGeneratorId]);

  useEffect(() => {
    load();
  }, [load]);

  // Real-time refresh hook: every AigcGenerationRun has a 1:1 BTask
  // (id = "AigcGenerationRun:<runId>") whose state is pushed via WebGuiHub SignalR
  // into the BTasks store. We watch the set + statuses of those tasks; whenever the
  // fingerprint changes (new run appears, status transitions Pending→Running→…), we
  // reload. Percentage ticks don't change the fingerprint, so we don't re-fetch on
  // every progress update.
  const bTasks = useBTasksStore(selectTasks);
  const aigcBTaskFingerprint = useMemo(
    () =>
      bTasks
        .filter((t) => typeof t.id === "string" && t.id.startsWith(AIGC_RUN_BTASK_PREFIX))
        .map((t) => `${t.id}:${t.status}`)
        .sort()
        .join("|"),
    [bTasks],
  );
  useEffect(() => {
    // Skip the initial-empty render so we don't double-fire alongside the mount load above.
    if (!aigcBTaskFingerprint) return;
    load();
  }, [aigcBTaskFingerprint, load]);

  const loadArtifacts = async (runId: number) => {
    if (artifactsByRun[runId]) return;
    const r = await BApi.aigc.getAigcArtifacts({ runId } as any);
    if (!r.code && r.data) {
      setArtifactsByRun((prev) => ({ ...prev, [runId]: r.data! }));
    }
  };

  const generatorName = (id: number) => generators.find((g) => g.id === id)?.name ?? `#${id}`;
  const tStatus = (n: number) => t<string>(`aigc.runStatus.${n}`);

  const handleDeleteRun = async (id: number) => {
    const r = await BApi.aigc.deleteAigcRun(id);
    if (!r.code) {
      toast.success(t<string>("common.success.saved"));
      setArtifactsByRun((prev) => {
        const n = { ...prev };
        delete n[id];
        return n;
      });
      await load();
    } else {
      toast.danger(r.message || t<string>("aigc.records.deleteFailed"));
    }
  };

  const handleStopRun = async (id: number) => {
    const r = await BApi.aigc.stopAigcRun(id);
    if (!r.code) {
      toast.success(t<string>("aigc.records.stopRequested"));
      await load();
    } else {
      toast.danger(r.message || t<string>("aigc.records.stopFailed"));
    }
  };

  const handleDeleteArtifact = (runId: number, artifactId: number) => {
    createPortal(ConfirmModal, {
      title: t<string>("common.action.delete"),
      message: t<string>("aigc.records.confirmDeleteArtifact"),
      destructive: true,
      onConfirm: async () => {
        const r = await BApi.aigc.deleteAigcArtifact(artifactId);
        if (!r.code) {
          toast.success(t<string>("common.success.saved"));
          setArtifactsByRun((prev) => {
            const n = { ...prev };
            delete n[runId];
            return n;
          });
          await load();
        } else {
          toast.danger(r.message || t<string>("aigc.records.deleteFailed"));
        }
      },
    });
  };

  return (
    <div className="flex flex-col gap-4 p-4">
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-xl font-semibold">{t<string>("aigc.records.title")}</h1>
        <div className="w-64">
          <Select
            label={t<string>("aigc.records.filterByConfig")}
            size="sm"
            selectedKeys={filterGeneratorId ? [String(filterGeneratorId)] : []}
            onSelectionChange={(keys) => {
              const v = Array.from(keys)[0];
              setFilterGeneratorId(v ? Number(v) : null);
            }}
            dataSource={[
              { label: t<string>("aigc.records.allConfigs"), value: "" },
              ...generators.map((g) => ({ label: g.name, value: String(g.id) })),
            ]}
          />
        </div>
      </div>

      <Accordion
        variant="splitted"
        onSelectionChange={(keys) => {
          if (keys === "all") return;
          const set = keys as Set<string>;
          set.forEach((k) => loadArtifacts(Number(k)));
        }}
      >
        {runs.map((r) => {
          const arts = artifactsByRun[r.id] ?? [];
          return (
            <AccordionItem
              key={r.id}
              aria-label={`Run ${r.id}`}
              title={
                <div className="flex items-center gap-3 flex-wrap">
                  <span className="font-mono">#{r.id}</span>
                  {/* Inline actions. Wrapper stops propagation so clicks don't toggle the accordion. */}
                  <div className="flex items-center gap-1" onClick={(e) => e.stopPropagation()}>
                    {isStoppable(r.status) && (
                      <Button
                        size="sm"
                        isIconOnly
                        variant="light"
                        color="warning"
                        title={t<string>("aigc.records.stopRun")}
                        onPress={() => handleStopRun(r.id)}
                      >
                        <AiOutlineStop className="text-lg" />
                      </Button>
                    )}
                    <Button
                      size="sm"
                      isIconOnly
                      variant="light"
                      color="danger"
                      title={t<string>("aigc.records.deleteRun")}
                      onPress={() => handleDeleteRun(r.id)}
                    >
                      <AiOutlineDelete className="text-lg" />
                    </Button>
                  </div>
                  <Chip size="sm" color={STATUS_COLORS[r.status] ?? "default"} variant="dot">
                    {tStatus(r.status)}
                  </Chip>
                  <span className="text-sm text-default-500">{generatorName(r.generatorId)}</span>
                  {r.createdAt && (
                    <span className="text-xs text-default-400">
                      {new Date(r.createdAt).toLocaleString()}
                    </span>
                  )}
                  {r.errorMessage && (
                    <span className="text-xs text-danger truncate max-w-md">{r.errorMessage}</span>
                  )}
                </div>
              }
            >
              <div className="flex flex-col gap-3">
                {r.prompt && (
                  <div>
                    <div className="text-xs text-default-400">
                      {t<string>("aigc.records.prompt")}
                    </div>
                    <div className="text-sm whitespace-pre-wrap">{r.prompt}</div>
                  </div>
                )}
                <Table removeWrapper aria-label={`artifacts for run ${r.id}`}>
                  <TableHeader>
                    <TableColumn>#</TableColumn>
                    <TableColumn>{t<string>("aigc.records.path")}</TableColumn>
                    <TableColumn>{t<string>("aigc.records.resource")}</TableColumn>
                    <TableColumn>&nbsp;</TableColumn>
                  </TableHeader>
                  <TableBody emptyContent={t<string>("common.empty")}>
                    {arts.map((a) => (
                      <TableRow key={a.id}>
                        <TableCell>{a.ordinalInRun}</TableCell>
                        <TableCell>
                          <span className="font-mono text-xs">{a.relativePath}</span>
                        </TableCell>
                        <TableCell>{a.resourceId ? `#${a.resourceId}` : "-"}</TableCell>
                        <TableCell>
                          <Button
                            size="sm"
                            variant="flat"
                            color="danger"
                            startContent={<AiOutlineDelete />}
                            onPress={() => handleDeleteArtifact(r.id, a.id)}
                          >
                            {t<string>("common.action.delete")}
                          </Button>
                        </TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </div>
            </AccordionItem>
          );
        })}
      </Accordion>
    </div>
  );
};

export default AigcRecordsPage;
