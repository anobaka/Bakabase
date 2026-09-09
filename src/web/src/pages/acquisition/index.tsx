"use client";

import type { components } from "@/sdk/BApi2";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";

import InboxDrawer from "./components/InboxDrawer";
import AcquisitionRow from "./components/AcquisitionRow";
import StartAcquisitionModal from "./components/StartAcquisitionModal";
import SetupWizard from "./components/SetupWizard";

import BApi from "@/sdk/BApi";
import { Button, Chip, Spinner, Tab, Tabs } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { AcquisitionStatus } from "@/sdk/constants";

export type AcquisitionTaskVm =
  components["schemas"]["Bakabase.Modules.Acquisition.Abstractions.Models.Domain.AcquisitionTask"];
export type AcquisitionRecipeVm =
  components["schemas"]["Bakabase.Modules.Acquisition.Abstractions.Services.AcquisitionRecipeSummary"];

/** Anything that has not finished. Waiting is emphatically one of them. */
const LIVE: AcquisitionStatus[] = [
  AcquisitionStatus.Waiting,
  AcquisitionStatus.Running,
  AcquisitionStatus.Pending,
];

/** Waiting first: it is the only state that needs a person, and it is what the page is for. */
const ORDER: Record<number, number> = {
  [AcquisitionStatus.Waiting]: 0,
  [AcquisitionStatus.Running]: 1,
  [AcquisitionStatus.Pending]: 2,
};

const AcquisitionPage: React.FC = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { createPortal } = useBakabaseContext();

  const [tasks, setTasks] = useState<AcquisitionTaskVm[]>([]);
  const [recipes, setRecipes] = useState<AcquisitionRecipeVm[]>([]);
  const [loading, setLoading] = useState(true);
  const [tab, setTab] = useState<"live" | "all">("live");

  const load = useCallback(async () => {
    try {
      const [tasksRsp, recipesRsp] = await Promise.all([
        BApi.acquisition.searchAcquisitions({}),
        BApi.acquisition.getAcquisitionRecipes(),
      ]);

      setTasks((tasksRsp.data ?? []) as AcquisitionTaskVm[]);
      setRecipes((recipesRsp.data ?? []) as AcquisitionRecipeVm[]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  // The queue runs on a timer server-side, so the page has to look again to see it move.
  useEffect(() => {
    const handle = setInterval(() => void load(), 5000);

    return () => clearInterval(handle);
  }, [load]);

  const shown = (tab === "live" ? tasks.filter((x) => LIVE.includes(x.status)) : tasks).sort(
    (a, b) => (ORDER[a.status] ?? 9) - (ORDER[b.status] ?? 9) || b.id - a.id,
  );

  const waitingCount = tasks.filter((x) => x.status === AcquisitionStatus.Waiting).length;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center gap-2">
        <Tabs selectedKey={tab} size="sm" onSelectionChange={(k) => setTab(k as "live" | "all")}>
          <Tab key="live" title={t<string>("acquisition.tab.live")} />
          <Tab key="all" title={t<string>("acquisition.tab.all")} />
        </Tabs>

        {waitingCount > 0 && (
          <Chip color="secondary" size="sm" variant="flat">
            {t<string>("acquisition.waitingCount", { count: waitingCount })}
          </Chip>
        )}

        <div className="ml-auto flex items-center gap-2">
          <Button
            size="sm"
            variant="light"
            onPress={() => createPortal(SetupWizard, { onDone: load })}
          >
            {t<string>("acquisition.setup.open")}
          </Button>
          <Button
            size="sm"
            variant="flat"
            onPress={() => createPortal(InboxDrawer, { onClaimed: load })}
          >
            {t<string>("acquisition.inbox.open")}
          </Button>
          <Button
            color="primary"
            size="sm"
            onPress={() => createPortal(StartAcquisitionModal, { recipes, onStarted: load })}
          >
            {t<string>("acquisition.start")}
          </Button>
        </div>
      </div>

      {loading && tasks.length === 0 ? (
        <div className="flex justify-center py-10">
          <Spinner size="lg" />
        </div>
      ) : shown.length === 0 ? (
        <div className="text-center text-default-500 py-10">
          {t<string>(tab === "live" ? "acquisition.empty.live" : "acquisition.empty.all")}
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          {shown.map((task) => (
            <AcquisitionRow
              key={task.id}
              recipes={recipes}
              task={task}
              onChanged={load}
              onEditRecipe={(id) => navigate(`/workflows/editor?id=${id}`)}
            />
          ))}
        </div>
      )}
    </div>
  );
};

export default AcquisitionPage;
