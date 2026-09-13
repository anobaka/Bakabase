"use client";

import type { Resource } from "@/core/models/Resource";
import type { components } from "@/sdk/BApi2";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import {
  AiOutlineApartment,
  AiOutlineCloudDownload,
  AiOutlineDelete,
  AiOutlineFolderOpen,
  AiOutlineLink,
  AiOutlinePlus,
  AiOutlineReload,
  AiOutlineUnorderedList,
} from "react-icons/ai";

import AddSourceModal from "./AddSourceModal";

import { Button, Chip, Modal, Select, Spinner, toast } from "@/components/bakaui";
import { FileSystemSelectorModal } from "@/components/FileSystemSelector";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { recipeLabel, stepLabel } from "@/pages/acquisition/recipeLabels";
import BApi from "@/sdk/BApi";
import { AcquisitionLeadKind } from "@/sdk/constants";

type Page = components["schemas"]["Bakabase.Service.Models.View.AcquisitionCandidatePageViewModel"];
type Lead = components["schemas"]["Bakabase.Service.Models.View.AcquisitionCandidateLeadViewModel"];

interface Props {
  resource: Resource;
  onChanged?: () => void;
  /** Close the containing dialog before navigating to workflows or tasks. */
  onNavigate?: () => void;
}

const leadKey = (lead: Lead) => `${lead.kind}-${lead.isDerived}-${lead.id}-${lead.value}`;
const KIND_LABELS: Partial<Record<AcquisitionLeadKind, string>> = {
  [AcquisitionLeadKind.SharedPage]: "acquisition.leads.kind.sharedPage",
  [AcquisitionLeadKind.SharedDocument]: "acquisition.leads.kind.sharedDocument",
  [AcquisitionLeadKind.DirectUrl]: "acquisition.leads.kind.directUrl",
  [AcquisitionLeadKind.Magnet]: "acquisition.leads.kind.magnet",
  [AcquisitionLeadKind.PlatformHolding]: "acquisition.leads.kind.platform",
  [AcquisitionLeadKind.Manual]: "acquisition.leads.kind.local",
};

/** A task-oriented acquisition module for resources without a local path. */
const AcquisitionPanel: React.FC<Props> = ({ resource, onChanged, onNavigate }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const navigate = useNavigate();
  const [data, setData] = useState<Page>();
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [revision, setRevision] = useState(0);
  const [pending, setPending] = useState<string>();
  const [selectedRecipes, setSelectedRecipes] = useState<Record<string, number>>({});

  useEffect(() => {
    if (resource.hasLocalPath) return;
    let active = true;

    setLoading(true);
    setFailed(false);
    setData(undefined);
    void (async () => {
      try {
        const rsp = await BApi.acquisition.getAcquisitionCandidate(resource.id);

        if (!active) return;
        if (rsp.code || !rsp.data) throw new Error("Unable to load acquisition methods");
        setData(rsp.data);
      } catch {
        if (active) setFailed(true);
      } finally {
        if (active) setLoading(false);
      }
    })();

    return () => {
      active = false;
    };
  }, [resource.id, resource.hasLocalPath, revision]);

  const refresh = () => setRevision((value) => value + 1);
  const openPage = (path: string) => {
    onNavigate?.();
    navigate(path);
  };
  const candidate = data?.items.find((item) => item.resourceId === resource.id);
  const activeTask = candidate?.activeTaskId != null;

  const acquire = async (lead: Lead, recipeId: number) => {
    setPending(leadKey(lead));
    try {
      const rsp = await BApi.acquisition.createAcquisition({
        resourceId: resource.id,
        acquisitionLeadId: lead.isDerived ? undefined : lead.id,
        leadKind: lead.kind,
        leadValue: lead.value,
        recipeDefinitionId: recipeId,
      });

      if (!rsp.code) toast.success(t<string>("acquisition.action.acquireStarted"));
    } catch {
      // The API client reports request errors; leave this panel available for retry.
    } finally {
      setPending(undefined);
      refresh();
    }
  };

  const deleteLead = async (lead: Lead) => {
    setPending(leadKey(lead));
    try {
      const rsp = await BApi.resource.deleteResourceAcquisitionLead(resource.id, lead.id);

      if (!rsp.code) refresh();
    } catch {
      // The API client reports request errors.
    } finally {
      setPending(undefined);
    }
  };

  const materialize = (path: string, mergeIfOccupied: boolean) =>
    BApi.resource.materializeResource(resource.id, { path, mergeIfOccupied });

  const linkLocalFolder = () => {
    createPortal(FileSystemSelectorModal, {
      targetType: "folder",
      onSelected: async (e: any) => {
        const path = e.path as string;
        const rsp = await materialize(path, false);

        if (rsp.code) {
          toast.danger(rsp.message ?? t<string>("acquisition.error.materializeFailed"));

          return;
        }

        // The folder already belongs to another resource. Merging deletes that one, so it is put
        // to the user in words rather than done quietly.
        if (rsp.data && !rsp.data.materialized) {
          createPortal(Modal, {
            defaultVisible: true,
            title: t<string>("acquisition.materialize.conflict.title"),
            children: t<string>("acquisition.materialize.conflict.message", {
              name: rsp.data.occupiedByResourceName ?? `#${rsp.data.occupiedByResourceId}`,
            }),
            okProps: { color: "danger" },
            onOk: async () => {
              const merged = await materialize(path, true);

              if (merged.code) {
                toast.danger(merged.message ?? t<string>("acquisition.error.materializeFailed"));

                return;
              }
              toast.success(t<string>("acquisition.materialize.success"));
              onChanged?.();
            },
          });

          return;
        }

        toast.success(t<string>("acquisition.materialize.success"));
        onChanged?.();
      },
    });
  };

  const renderLead = (lead: Lead) => {
    const key = `${resource.id}-${leadKey(lead)}`;
    const recipes = (data?.recipes ?? []).filter((recipe) =>
      lead.applicableRecipeDefinitionIds.includes(recipe.definitionId),
    );
    const selectedId = recipes.some((recipe) => recipe.definitionId === selectedRecipes[key])
      ? selectedRecipes[key]
      : recipes.some((recipe) => recipe.definitionId === lead.defaultRecipeDefinitionId)
        ? lead.defaultRecipeDefinitionId
        : undefined;
    const selected = recipes.find((recipe) => recipe.definitionId === selectedId);
    const supported = lead.capability === "supported" && recipes.length > 0;

    return (
      <div
        key={key}
        className="flex min-w-0 flex-col gap-2 rounded-xl border border-default-200 bg-content1 p-3"
      >
        <div className="flex flex-wrap items-center gap-2">
          <AiOutlineLink aria-hidden className="shrink-0 text-base text-default-500" />
          <span className="min-w-0 flex-1 text-sm font-medium">
            {lead.isDerived && lead.sourceName
              ? lead.sourceName
              : t<string>(KIND_LABELS[lead.kind] ?? "acquisition.leads.kind.other")}
          </span>
          <Chip color={supported ? "default" : "warning"} size="sm" variant="flat">
            {t<string>(
              supported ? "acquisition.overview.unverified" : "acquisition.overview.unsupported",
            )}
          </Chip>
          {!lead.isDerived && (
            <Button
              isIconOnly
              aria-label={t<string>("acquisition.leads.remove")}
              isDisabled={pending != null || activeTask}
              size="sm"
              variant="light"
              onPress={() => void deleteLead(lead)}
            >
              <AiOutlineDelete aria-hidden className="text-base" />
            </Button>
          )}
        </div>
        {lead.isDerived ? (
          <p className="text-xs text-default-500">
            {t<string>("acquisition.leads.platformDescription", { source: lead.sourceName })}
          </p>
        ) : (
          <p className="line-clamp-2 break-all text-xs text-default-500" title={lead.value}>
            {lead.value}
          </p>
        )}
        {!supported && (
          <p className="text-xs text-warning-600">
            {t<string>(
              lead.capability === "unsupportedPlatform"
                ? "acquisition.overview.unsupportedPlatform"
                : "acquisition.overview.noApplicableRecipe",
            )}
          </p>
        )}
        {recipes.length > 1 || (recipes.length > 0 && !selected) ? (
          <Select
            disallowEmptySelection
            aria-label={t<string>("acquisition.overview.selectRecipe")}
            dataSource={recipes.map((recipe) => ({
              value: String(recipe.definitionId),
              label: recipeLabel(recipe, t),
            }))}
            placeholder={t<string>("acquisition.overview.selectRecipe")}
            selectedKeys={selectedId == null ? [] : [String(selectedId)]}
            size="sm"
            onSelectionChange={(keys) => {
              const value = Array.from(keys)[0];

              if (value != null)
                setSelectedRecipes((current) => ({ ...current, [key]: Number(value) }));
            }}
          />
        ) : selected ? (
          <div className="flex items-center gap-2 text-xs text-default-600">
            <AiOutlineApartment aria-hidden className="text-base" />
            {recipeLabel(selected, t)}
          </div>
        ) : null}
        {supported && !selected && (
          <p className="text-xs text-warning-600">
            {t<string>("acquisition.overview.defaultUnavailable")}
          </p>
        )}
        <details className="text-xs text-default-500">
          <summary className="w-fit cursor-pointer rounded py-1 hover:text-foreground">
            {t<string>("acquisition.overview.sourceDetails")}
          </summary>
          <div className="mt-1 flex flex-col gap-2 rounded-lg bg-default-100 p-3">
            <p className="max-h-24 overflow-auto break-all">{lead.value}</p>
            {lead.note && <p>{lead.note}</p>}
            {selected && (
              <>
                <p>{selected.stepKinds.map((kind) => stepLabel(kind, t)).join(" → ")}</p>
                <Button
                  className="h-auto min-w-0 self-start px-0 py-1"
                  size="sm"
                  startContent={<AiOutlineApartment aria-hidden className="text-base" />}
                  variant="light"
                  onPress={() => openPage(`/workflows/editor?id=${selected.definitionId}`)}
                >
                  {t<string>("acquisition.recipes.open")}
                </Button>
              </>
            )}
          </div>
        </details>
        <Button
          className="self-end"
          color="primary"
          isDisabled={!supported || !selected || activeTask || pending != null || loading}
          isLoading={pending === leadKey(lead)}
          size="sm"
          startContent={<AiOutlineCloudDownload aria-hidden className="text-base" />}
          variant="flat"
          onPress={() => {
            if (selected) void acquire(lead, selected.definitionId);
          }}
        >
          {t<string>(activeTask ? "acquisition.overview.acquiring" : "acquisition.overview.start")}
        </Button>
      </div>
    );
  };

  // This component also appears outside the detail layout, so guard its own local-file state.
  if (resource.hasLocalPath || (!loading && !failed && data && !candidate)) return null;

  return (
    <section className="flex flex-col gap-3 rounded-xl border border-default-200 bg-default-50/60 p-4">
      <div className="flex items-start gap-3">
        <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
          <AiOutlineCloudDownload aria-hidden className="text-xl" />
        </span>
        <div className="min-w-0 flex-1">
          <h3 className="text-sm font-medium">{t<string>("acquisition.leads.title")}</h3>
          <p className="mt-1 text-xs leading-relaxed text-default-500">
            {t<string>("acquisition.leads.description")}
          </p>
        </div>
      </div>
      <p className="text-xs leading-relaxed text-default-500">
        {t<string>("acquisition.leads.visibilityHint")}
      </p>
      {loading ? (
        <Spinner aria-label={t<string>("acquisition.leads.loading")} size="sm" />
      ) : failed ? (
        <div className="flex items-center gap-2 text-sm text-default-500" role="alert">
          {t<string>("acquisition.leads.loadFailed")}
          <Button
            size="sm"
            startContent={<AiOutlineReload aria-hidden className="text-base" />}
            variant="flat"
            onPress={refresh}
          >
            {t<string>("acquisition.retry")}
          </Button>
        </div>
      ) : (
        <>
          {activeTask && (
            <div className="flex flex-wrap items-center justify-between gap-2 text-xs text-default-500">
              {t<string>("acquisition.leads.taskActive")}
              <Button
                size="sm"
                startContent={<AiOutlineUnorderedList aria-hidden className="text-base" />}
                variant="flat"
                onPress={() => openPage("/acquisitions?tab=live")}
              >
                {t<string>("acquisition.overview.viewTask")}
              </Button>
            </div>
          )}
          {candidate?.leads.length ? (
            candidate.leads.map(renderLead)
          ) : (
            <p className="text-sm text-default-500">{t<string>("acquisition.leads.empty")}</p>
          )}
        </>
      )}
      <div className="grid gap-3 border-t border-default-200 pt-3 sm:grid-cols-2">
        <div className="flex flex-col items-start gap-2">
          <Button
            color="primary"
            isDisabled={pending != null || activeTask || loading || failed}
            size="sm"
            startContent={<AiOutlinePlus aria-hidden className="text-base" />}
            variant="flat"
            onPress={() =>
              createPortal(AddSourceModal, { resourceId: resource.id, onAdded: refresh })
            }
          >
            {t<string>("acquisition.leads.chooseMethod")}
          </Button>
          <p className="text-xs leading-relaxed text-default-500">
            {t<string>("acquisition.leads.addMethodHint")}
          </p>
        </div>
        <div className="flex flex-col items-start gap-2">
          <Button
            isDisabled={pending != null || activeTask}
            size="sm"
            startContent={<AiOutlineFolderOpen aria-hidden className="text-base" />}
            variant="flat"
            onPress={linkLocalFolder}
          >
            {t<string>("acquisition.action.linkLocalFolder")}
          </Button>
          <p className="text-xs leading-relaxed text-default-500">
            {t<string>("acquisition.leads.linkLocalHint")}
          </p>
        </div>
      </div>
    </section>
  );
};

AcquisitionPanel.displayName = "AcquisitionPanel";

export default AcquisitionPanel;
