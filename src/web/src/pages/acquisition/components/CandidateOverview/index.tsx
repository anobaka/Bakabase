import type { components } from "@/sdk/BApi2";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlineReload } from "react-icons/ai";

import { recipeLabel, stepLabel } from "../../recipeLabels";

import BApi from "@/sdk/BApi";
import { Button, Chip, Input, Pagination, Select, Spinner, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import ResourceDetailModal from "@/components/Resource/components/DetailModal";
import { AcquisitionLeadKind } from "@/sdk/constants";

type Page = components["schemas"]["Bakabase.Service.Models.View.AcquisitionCandidatePageViewModel"];
type Candidate =
  components["schemas"]["Bakabase.Service.Models.View.AcquisitionCandidateViewModel"];
type Lead = components["schemas"]["Bakabase.Service.Models.View.AcquisitionCandidateLeadViewModel"];

type Props = {
  onStarted: () => void;
  onViewTasks: () => void;
  onOpenRecipe: (id: number) => void;
};

const PAGE_SIZE = 24;
const FILTERS = ["all", "withSources", "withoutSources", "unsupported"];
const METHODS: Record<string, string> = {
  sharedContent: "acquisition.overview.method.sharedContent",
  directDownload: "acquisition.overview.method.directDownload",
  inbox: "acquisition.overview.method.inbox",
  platformDownload: "acquisition.overview.method.platformDownload",
  platformInstall: "acquisition.overview.method.platformInstall",
  localDirectory: "acquisition.overview.method.localDirectory",
  magnetDownload: "acquisition.overview.method.magnetDownload",
};

const leadKey = (resourceId: number, lead: Lead) =>
  `${resourceId}-${lead.kind}-${lead.id}-${lead.value}`;

const CandidateOverview = ({ onStarted, onViewTasks, onOpenRecipe }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [search, setSearch] = useState("");
  const [keyword, setKeyword] = useState("");
  const [filter, setFilter] = useState("all");
  const [page, setPage] = useState(1);
  const [revision, setRevision] = useState(0);
  const [data, setData] = useState<Page>();
  const [loading, setLoading] = useState(true);
  const [failed, setFailed] = useState(false);
  const [selectedRecipes, setSelectedRecipes] = useState<Record<string, number>>({});
  const [starting, setStarting] = useState<string>();

  useEffect(() => {
    let active = true;

    setLoading(true);
    setFailed(false);
    const load = async () => {
      try {
        const response = await BApi.acquisition.searchAcquisitionCandidates({
          keyword: keyword || undefined,
          page,
          pageSize: PAGE_SIZE,
          filter,
        });

        if (!active) return;
        if (response.code || !response.data)
          throw new Error("Unable to load acquisition candidates");
        const lastPage = Math.max(1, Math.ceil(response.data.totalCount / PAGE_SIZE));

        if (page > lastPage) {
          setPage(lastPage);
        } else {
          setData(response.data);
        }
      } catch {
        if (active) {
          setData(undefined);
          setFailed(true);
        }
      } finally {
        if (active) setLoading(false);
      }
    };

    void load();

    return () => {
      active = false;
    };
  }, [filter, keyword, page, revision]);

  const refresh = () => setRevision((value) => value + 1);
  const openResource = (resourceId: number) => {
    createPortal(ResourceDetailModal, { id: resourceId, onDestroyed: refresh });
  };

  const start = async (candidate: Candidate, lead: Lead, recipeId: number) => {
    setStarting(leadKey(candidate.resourceId, lead));
    try {
      const response = await BApi.acquisition.createAcquisition({
        resourceId: candidate.resourceId,
        acquisitionLeadId: lead.isDerived ? undefined : lead.id,
        leadKind: lead.kind,
        leadValue: lead.value,
        recipeDefinitionId: recipeId,
      });

      if (!response.code) {
        toast.success(t<string>("acquisition.started"));
        onStarted();
      }
    } catch {
      // The API client reports the request failure. Keep the overview available to retry.
    } finally {
      setStarting(undefined);
      refresh();
    }
  };

  const sourceLabel = (lead: Lead) => {
    if (lead.sourceName) return lead.sourceName;
    try {
      const url = new URL(lead.value);

      if (url.hostname) return url.hostname;
    } catch {
      // Shared documents and manually supplied references need not be URLs.
    }

    return t<string>(
      lead.kind === AcquisitionLeadKind.Magnet
        ? "acquisition.overview.source.magnet"
        : "acquisition.overview.source.link",
    );
  };

  const renderLead = (candidate: Candidate, lead: Lead) => {
    const key = leadKey(candidate.resourceId, lead);
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
    const defaultUnavailable = !recipes.some(
      (recipe) => recipe.definitionId === lead.defaultRecipeDefinitionId,
    );
    const active = candidate.activeTaskId != null;

    return (
      <div
        key={key}
        className="grid gap-4 border-t border-default-200 py-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)_minmax(0,1fr)_auto]"
      >
        <div className="min-w-0">
          <div className="mb-1 text-xs text-default-400 lg:hidden">
            {t<string>("acquisition.overview.column.source")}
          </div>
          <div className="text-sm font-medium">{sourceLabel(lead)}</div>
          <div className="mt-1 max-h-24 overflow-auto break-all text-xs text-default-500">
            {lead.value}
          </div>
          {lead.note && <p className="mt-1 text-xs text-default-500">{lead.note}</p>}
        </div>
        <div className="min-w-0">
          <div className="mb-1 text-xs text-default-400 lg:hidden">
            {t<string>("acquisition.overview.column.recipe")}
          </div>
          {recipes.length > 1 || (recipes.length > 0 && defaultUnavailable) ? (
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
          ) : (
            <div className="text-sm font-medium">
              {selected ? recipeLabel(selected, t) : t<string>("acquisition.overview.noRecipe")}
            </div>
          )}
          {supported && defaultUnavailable && (
            <p className="mt-2 text-xs text-warning-600">
              {t<string>("acquisition.overview.defaultUnavailable")}
            </p>
          )}
          {selected && (
            <>
              <div className="mt-2 text-xs leading-relaxed text-default-500">
                {selected.stepKinds.map((kind) => stepLabel(kind, t)).join(" → ")}
              </div>
              <Button
                className="mt-1 h-auto min-w-0 px-0 py-1"
                size="sm"
                variant="light"
                onPress={() => onOpenRecipe(selected.definitionId)}
              >
                {t<string>("acquisition.recipes.open")}
              </Button>
            </>
          )}
        </div>
        <div className="flex flex-col items-start gap-2">
          <Chip color={supported ? "default" : "warning"} size="sm" variant="flat">
            {t<string>(
              supported ? "acquisition.overview.unverified" : "acquisition.overview.unsupported",
            )}
          </Chip>
          <p className="text-xs leading-relaxed text-default-500">
            {t<string>(
              lead.capability === "unsupportedPlatform"
                ? "acquisition.overview.unsupportedPlatform"
                : lead.capability === "noApplicableRecipe" || recipes.length === 0
                  ? "acquisition.overview.noApplicableRecipe"
                  : !selected
                    ? "acquisition.overview.selectRecipeFirst"
                    : selected.definitionId !== lead.defaultRecipeDefinitionId
                      ? "acquisition.overview.method.selectedRecipe"
                      : (METHODS[lead.method] ?? "acquisition.overview.unverifiedDescription"),
            )}
          </p>
        </div>
        <div className="flex items-start">
          <Button
            color="primary"
            isDisabled={!supported || !selected || active || starting != null || loading}
            isLoading={starting === key}
            size="sm"
            variant="flat"
            onPress={() => {
              if (selected) void start(candidate, lead, selected.definitionId);
            }}
          >
            {t<string>(active ? "acquisition.overview.acquiring" : "acquisition.overview.start")}
          </Button>
        </div>
      </div>
    );
  };

  return (
    <div className="flex flex-col gap-4">
      <div className="rounded-lg bg-default-100 px-4 py-3 text-sm text-default-600">
        {t<string>("acquisition.overview.unverifiedDescription")}
      </div>
      <form
        className="flex flex-wrap items-end gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          setKeyword(search.trim());
          setPage(1);
        }}
      >
        <Input
          className="min-w-48 flex-1"
          label={t<string>("acquisition.overview.search")}
          size="sm"
          value={search}
          onValueChange={setSearch}
        />
        <Select
          disallowEmptySelection
          aria-label={t<string>("acquisition.overview.filter")}
          className="w-48"
          dataSource={FILTERS.map((value) => ({
            value,
            label: t<string>(`acquisition.overview.filter.${value}`),
          }))}
          selectedKeys={[filter]}
          size="sm"
          onSelectionChange={(keys) => {
            const value = Array.from(keys)[0];

            if (value != null) {
              setFilter(String(value));
              setPage(1);
            }
          }}
        />
        <Button size="sm" type="submit" variant="flat">
          {t<string>("acquisition.overview.searchAction")}
        </Button>
        <Button
          isIconOnly
          aria-label={t<string>("acquisition.overview.refresh")}
          isDisabled={loading}
          size="sm"
          variant="light"
          onPress={refresh}
        >
          <AiOutlineReload />
        </Button>
      </form>
      {failed ? (
        <div className="flex flex-col items-center gap-3 py-10 text-default-500" role="alert">
          <p>{t<string>("acquisition.overview.loadFailed")}</p>
          <Button size="sm" onPress={refresh}>
            {t<string>("acquisition.retry")}
          </Button>
        </div>
      ) : loading ? (
        <div className="flex justify-center py-10">
          <Spinner aria-label={t<string>("acquisition.overview.loading")} />
        </div>
      ) : !data?.items.length ? (
        <div className="py-10 text-center text-default-500">
          {t<string>("acquisition.overview.empty")}
        </div>
      ) : (
        <>
          <div className="text-sm text-default-500">
            {t<string>("acquisition.overview.count", { count: data.totalCount })}
          </div>
          {data.items.map((candidate) => (
            <section
              key={candidate.resourceId}
              className="rounded-xl border border-default-200 px-4"
            >
              <div className="flex flex-wrap items-center gap-2 py-3">
                <Button
                  className="h-auto min-w-0 whitespace-normal px-0 text-left text-base font-medium"
                  variant="light"
                  onPress={() => openResource(candidate.resourceId)}
                >
                  {candidate.resourceName ||
                    t<string>("acquisition.unnamed", { id: candidate.resourceId })}
                </Button>
                {candidate.activeTaskId != null && (
                  <Chip color="secondary" size="sm" variant="flat">
                    {t<string>("acquisition.overview.acquiring")}
                  </Chip>
                )}
                <Button
                  className="ml-auto"
                  size="sm"
                  variant="light"
                  onPress={() => openResource(candidate.resourceId)}
                >
                  {t<string>("acquisition.overview.resourceDetails")}
                </Button>
                {candidate.activeTaskId != null && (
                  <Button size="sm" variant="flat" onPress={onViewTasks}>
                    {t<string>("acquisition.overview.viewTask")}
                  </Button>
                )}
              </div>
              {candidate.leads.length === 0 ? (
                <div className="border-t border-default-200 py-4">
                  <div className="text-sm font-medium">
                    {t<string>("acquisition.overview.noSources")}
                  </div>
                  <p className="mt-1 text-xs text-default-500">
                    {t<string>("acquisition.overview.noSourcesDescription")}
                  </p>
                </div>
              ) : (
                <>
                  <div className="hidden grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)_minmax(0,1fr)_auto] gap-4 pb-2 text-xs text-default-400 lg:grid">
                    <span>{t<string>("acquisition.overview.column.source")}</span>
                    <span>{t<string>("acquisition.overview.column.recipe")}</span>
                    <span>{t<string>("acquisition.overview.column.capability")}</span>
                    <span>{t<string>("acquisition.overview.column.action")}</span>
                  </div>
                  {candidate.leads.map((lead) => renderLead(candidate, lead))}
                </>
              )}
            </section>
          ))}
          {data.totalCount > PAGE_SIZE && (
            <Pagination
              aria-label={t<string>("acquisition.overview.pagination")}
              className="self-center"
              page={page}
              total={Math.ceil(data.totalCount / PAGE_SIZE)}
              onChange={setPage}
            />
          )}
        </>
      )}
    </div>
  );
};

export default CandidateOverview;
