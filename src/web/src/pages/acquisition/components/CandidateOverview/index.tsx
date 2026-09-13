import type { components } from "@/sdk/BApi2";

import React, { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  AiOutlineApartment,
  AiOutlineCloudDownload,
  AiOutlineFileText,
  AiOutlineInfoCircle,
  AiOutlineLink,
  AiOutlineReload,
  AiOutlineSearch,
  AiOutlineUnorderedList,
} from "react-icons/ai";

import { recipeLabel, stepLabel } from "../../recipeLabels";

import BApi from "@/sdk/BApi";
import {
  Button,
  Card,
  CardBody,
  CardHeader,
  Chip,
  Input,
  Pagination,
  Select,
  Spinner,
  Tooltip,
  toast,
} from "@/components/bakaui";
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
      <div key={key} className="flex min-w-0 flex-col gap-2 border-t border-default-200 pt-3">
        <div className="flex items-center gap-2">
          <AiOutlineLink aria-hidden className="shrink-0 text-base text-default-400" />
          <span className="min-w-0 flex-1 truncate text-sm font-medium" title={sourceLabel(lead)}>
            {sourceLabel(lead)}
          </span>
          <Chip color={supported ? "default" : "warning"} size="sm" variant="flat">
            {t<string>(
              supported ? "acquisition.overview.unverified" : "acquisition.overview.unsupported",
            )}
          </Chip>
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
          <div className="flex items-center gap-2 text-sm text-default-600">
            <AiOutlineApartment aria-hidden className="shrink-0 text-base" />
            {selected ? recipeLabel(selected, t) : t<string>("acquisition.overview.noRecipe")}
          </div>
        )}
        {supported && defaultUnavailable && !selected && (
          <p className="text-xs text-warning-600">
            {t<string>("acquisition.overview.defaultUnavailable")}
          </p>
        )}
        {!supported && (
          <p className="text-xs text-default-500">
            {t<string>(
              lead.capability === "unsupportedPlatform"
                ? "acquisition.overview.unsupportedPlatform"
                : "acquisition.overview.noApplicableRecipe",
            )}
          </p>
        )}
        <details className="text-xs text-default-500">
          <summary className="w-fit cursor-pointer rounded py-1 outline-offset-2 hover:text-foreground focus-visible:outline-2">
            {t<string>("acquisition.overview.sourceDetails")}
          </summary>
          <div className="mt-1 flex flex-col gap-2 rounded-lg bg-default-100 p-3">
            <div className="max-h-24 overflow-auto break-all">{lead.value}</div>
            {lead.note && <p className="break-words">{lead.note}</p>}
            {selected && (
              <>
                <div className="leading-relaxed">
                  {selected.stepKinds.map((kind) => stepLabel(kind, t)).join(" → ")}
                </div>
                <Button
                  className="h-auto min-w-0 self-start px-0 py-1"
                  size="sm"
                  startContent={<AiOutlineApartment aria-hidden className="text-base" />}
                  variant="light"
                  onPress={() => onOpenRecipe(selected.definitionId)}
                >
                  {t<string>("acquisition.recipes.open")}
                </Button>
              </>
            )}
            {supported && (
              <p className="leading-relaxed">
                {t<string>(
                  !selected
                    ? "acquisition.overview.selectRecipeFirst"
                    : selected.definitionId !== lead.defaultRecipeDefinitionId
                      ? "acquisition.overview.method.selectedRecipe"
                      : (METHODS[lead.method] ?? "acquisition.overview.unverifiedDescription"),
                )}
              </p>
            )}
          </div>
        </details>
        <Button
          className="self-end"
          color="primary"
          isDisabled={!supported || !selected || active || starting != null || loading}
          isLoading={starting === key}
          size="sm"
          startContent={<AiOutlineCloudDownload aria-hidden className="text-base" />}
          variant="flat"
          onPress={() => {
            if (selected) void start(candidate, lead, selected.definitionId);
          }}
        >
          {t<string>(active ? "acquisition.overview.acquiring" : "acquisition.overview.start")}
        </Button>
      </div>
    );
  };

  return (
    <div className="flex flex-col gap-4">
      <form
        className="flex flex-wrap items-center gap-2"
        onSubmit={(event) => {
          event.preventDefault();
          setKeyword(search.trim());
          setPage(1);
        }}
      >
        <Input
          aria-label={t<string>("acquisition.overview.search")}
          className="w-full sm:w-64"
          classNames={{ inputWrapper: "h-9 min-h-9" }}
          placeholder={t<string>("acquisition.overview.search")}
          size="sm"
          value={search}
          onValueChange={setSearch}
        />
        <Select
          disallowEmptySelection
          aria-label={t<string>("acquisition.overview.filter")}
          className="w-48 max-w-full"
          classNames={{ trigger: "h-9 min-h-9" }}
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
        <Button
          className="h-9"
          size="sm"
          startContent={<AiOutlineSearch aria-hidden className="text-lg" />}
          type="submit"
          variant="flat"
        >
          {t<string>("acquisition.overview.searchAction")}
        </Button>
        <Button
          isIconOnly
          aria-label={t<string>("acquisition.overview.refresh")}
          className="h-9 w-9 min-w-9"
          isDisabled={loading}
          size="sm"
          variant="light"
          onPress={refresh}
        >
          <AiOutlineReload aria-hidden className="text-xl" />
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
          <div className="flex items-center gap-1 text-sm text-default-500">
            {t<string>("acquisition.overview.count", { count: data.totalCount })}
            <Tooltip content={t<string>("acquisition.overview.noSourcesDescription")}>
              <Button
                isIconOnly
                aria-label={t<string>("acquisition.overview.sourcesHelp")}
                className="h-6 w-6 min-w-6"
                size="sm"
                variant="light"
              >
                <AiOutlineInfoCircle aria-hidden className="text-base" />
              </Button>
            </Tooltip>
          </div>
          <div className="grid grid-cols-[repeat(auto-fill,minmax(min(100%,20rem),1fr))] items-start gap-3">
            {data.items.map((candidate) => (
              <Card
                key={candidate.resourceId}
                as="section"
                className="min-w-0 border border-default-200"
                radius="lg"
                shadow="none"
              >
                <CardHeader className="items-start gap-2 p-3 pb-0">
                  <AiOutlineFileText
                    aria-hidden
                    className="mt-1 shrink-0 text-xl text-default-400"
                  />
                  <Button
                    className="h-auto min-w-0 flex-1 justify-start whitespace-normal px-0 py-0.5 text-left text-sm font-medium"
                    variant="light"
                    onPress={() => openResource(candidate.resourceId)}
                  >
                    <span
                      className="line-clamp-2 break-all"
                      title={candidate.resourceName ?? undefined}
                    >
                      {candidate.resourceName ||
                        t<string>("acquisition.unnamed", { id: candidate.resourceId })}
                    </span>
                  </Button>
                  {candidate.activeTaskId != null && (
                    <Button
                      className="shrink-0"
                      size="sm"
                      startContent={<AiOutlineUnorderedList aria-hidden className="text-base" />}
                      variant="flat"
                      onPress={onViewTasks}
                    >
                      {t<string>("acquisition.overview.viewTask")}
                    </Button>
                  )}
                </CardHeader>
                <CardBody className="gap-3 p-3">
                  {candidate.leads.length === 0 ? (
                    <div className="flex flex-col gap-2 border-t border-default-200 pt-3">
                      <span className="text-xs text-default-500">
                        {t<string>("acquisition.overview.noSources")}
                      </span>
                      <Button
                        className="self-end"
                        size="sm"
                        startContent={<AiOutlineLink aria-hidden className="text-base" />}
                        variant="flat"
                        onPress={() => openResource(candidate.resourceId)}
                      >
                        {t<string>("acquisition.overview.addSource")}
                      </Button>
                    </div>
                  ) : (
                    candidate.leads.map((lead) => renderLead(candidate, lead))
                  )}
                </CardBody>
              </Card>
            ))}
          </div>
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
