"use client";

import { useCallback, useEffect, useState } from "react";
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
import { AiOutlineDelete } from "react-icons/ai";

import { Select, toast } from "@/components/bakaui";
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
  1: "default",
  2: "primary",
  3: "success",
  4: "danger",
  5: "warning",
};

const AigcRecordsPage = () => {
  const { t } = useTranslation();
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
    if (!confirm(t<string>("aigc.records.confirmDeleteRun"))) return;
    const r = await BApi.aigc.deleteAigcRun(id);
    if (!r.code) {
      toast.success(t<string>("common.success.saved"));
      setArtifactsByRun((prev) => {
        const n = { ...prev };
        delete n[id];
        return n;
      });
      await load();
    }
  };

  const handleDeleteArtifact = async (runId: number, artifactId: number) => {
    if (!confirm(t<string>("aigc.records.confirmDeleteArtifact"))) return;
    const r = await BApi.aigc.deleteAigcArtifact(artifactId);
    if (!r.code) {
      toast.success(t<string>("common.success.saved"));
      setArtifactsByRun((prev) => {
        const n = { ...prev };
        delete n[runId];
        return n;
      });
      await load();
    }
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
                <div>
                  <Button
                    size="sm"
                    variant="flat"
                    color="danger"
                    startContent={<AiOutlineDelete />}
                    onPress={() => handleDeleteRun(r.id)}
                  >
                    {t<string>("aigc.records.deleteRun")}
                  </Button>
                </div>
              </div>
            </AccordionItem>
          );
        })}
      </Accordion>
    </div>
  );
};

export default AigcRecordsPage;
