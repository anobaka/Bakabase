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

const StatusLabels: Record<number, { label: string; color: "default" | "primary" | "success" | "danger" | "warning" }> = {
  1: { label: "Pending", color: "default" },
  2: { label: "Running", color: "primary" },
  3: { label: "Succeeded", color: "success" },
  4: { label: "Failed", color: "danger" },
  5: { label: "Imported", color: "warning" },
};

const AigcRunsPage = () => {
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

  const handleDeleteRun = async (id: number) => {
    if (!confirm("Delete this run, its artifacts and the related resources/files?")) return;
    const r = await BApi.aigc.deleteAigcRun(id);
    if (!r.code) {
      toast.success("Deleted");
      setArtifactsByRun((prev) => {
        const n = { ...prev };
        delete n[id];
        return n;
      });
      await load();
    }
  };

  const handleDeleteArtifact = async (runId: number, artifactId: number) => {
    if (!confirm("Delete this artifact (and its resource/file)?")) return;
    const r = await BApi.aigc.deleteAigcArtifact(artifactId);
    if (!r.code) {
      toast.success("Deleted");
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
        <h1 className="text-xl font-semibold">AIGC Runs</h1>
        <div className="w-64">
          <Select
            label="Filter by generator"
            selectedKeys={filterGeneratorId ? [String(filterGeneratorId)] : []}
            onSelectionChange={(keys) => {
              const v = Array.from(keys)[0];
              setFilterGeneratorId(v ? Number(v) : null);
            }}
            dataSource={[
              { label: "All", value: "" },
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
          const status = StatusLabels[r.status] ?? { label: String(r.status), color: "default" as const };
          const arts = artifactsByRun[r.id] ?? [];
          return (
            <AccordionItem
              key={r.id}
              aria-label={`Run ${r.id}`}
              title={
                <div className="flex items-center gap-3 flex-wrap">
                  <span className="font-mono">#{r.id}</span>
                  <Chip size="sm" color={status.color} variant="dot">{status.label}</Chip>
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
                    <div className="text-xs text-default-400">Prompt</div>
                    <div className="text-sm whitespace-pre-wrap">{r.prompt}</div>
                  </div>
                )}
                <Table removeWrapper aria-label={`artifacts for run ${r.id}`}>
                  <TableHeader>
                    <TableColumn>#</TableColumn>
                    <TableColumn>Path</TableColumn>
                    <TableColumn>Resource</TableColumn>
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
                            Delete
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
                    Delete run
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

export default AigcRunsPage;
