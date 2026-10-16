"use client";

import type { CollectionModel } from "@/stores/collections";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { AiOutlineArrowLeft, AiOutlineCloudDownload, AiOutlineSearch } from "react-icons/ai";

import CompositionBar from "../components/CompositionBar";
import ProgressChip from "../components/ProgressChip";
import { buildCollectionSearch } from "../helpers";

import MembersTab from "./components/MembersTab";
import SettingsTab from "./components/SettingsTab";
import SourceTab from "./components/SourceTab";

import {
  ResourceOwnerProvider,
  useResourceApi,
  useResourceOwner,
} from "@/features/management/resourceContext";
import { useManagementSources } from "@/features/management/useManagementSources";
import { canReadSourceMetadata } from "@/features/management/metadata";
import { CatalogOwnerMark, catalogOwnerSearch } from "@/features/management/ownedCatalog";
import { HelpCenterButton } from "@/components/HelpCenter";
import CollectionRuleEditor from "@/components/CollectionRuleEditor";
import { Button, Spinner, Tab, Tabs, Tooltip, toast } from "@/components/bakaui";
import { usePendingSearchStore } from "@/stores/pendingSearch";

/** A collection's resources, subscriptions, automatic inclusion criteria and settings. */
const CollectionDetailContent = () => {
  const BApi = useResourceApi();
  const owner = useResourceOwner();
  const readOnly = !!owner && !owner.isLocal && !owner.available;
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const [params] = useSearchParams();
  const setPendingSearch = usePendingSearchStore((s) => s.setPendingSearch);

  const id = Number(params.get("id"));
  const [collection, setCollection] = useState<CollectionModel>();
  const [error, setError] = useState<string>();
  const [loading, setLoading] = useState(true);
  // undefined means "not edited since the last save", which is also what disables the button.
  const [ruleDraft, setRuleDraft] = useState<string | null>();
  const [acquiring, setAcquiring] = useState(false);

  const load = useCallback(async () => {
    if (!id) {
      setLoading(false);

      return;
    }

    try {
      setError(undefined);
      const value = readOnly
        ? (await BApi.collection.getAllCollections({ withProgress: false })).data?.find(
            (item) => item.id === id,
          )
        : (await BApi.collection.getCollection(id)).data;

      setCollection((value ?? undefined) as CollectionModel | undefined);
    } catch (reason) {
      setError(String(reason));
    } finally {
      setLoading(false);
    }
  }, [id, BApi, readOnly]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) {
    return (
      <div className="flex justify-center py-16">
        <Spinner />
      </div>
    );
  }

  if (!collection) {
    return (
      <div className="flex flex-col items-center gap-3 py-16">
        <span className="text-default-400">{error || t<string>("collection.notFound")}</span>
        <Button size="sm" variant="light" onPress={() => navigate("/collections")}>
          {t<string>("collection.action.backToList")}
        </Button>
      </div>
    );
  }

  const openInResourcePage = () => {
    setPendingSearch({ ...buildCollectionSearch(collection), ...catalogOwnerSearch(owner) } as any);

    if (location.pathname !== "/resource") navigate("/resource");
  };

  /**
   * The list of missing members is already on screen; turning it into a queue should not mean
   * clicking each one. Members with nowhere to be got from are counted, not complained about.
   */
  const acquireMissing = async () => {
    setAcquiring(true);
    try {
      const result = (await BApi.collection.acquireMissingCollectionMembers(collection.id)).data;

      if (!result) return;

      if (result.started > 0) {
        toast.success(t<string>("collection.acquireMissing.started", { count: result.started }));
      } else if (result.withoutLead > 0) {
        toast.warning(
          t<string>("collection.acquireMissing.noLeads", { count: result.withoutLead }),
        );
      } else {
        toast.default(t<string>("collection.acquireMissing.nothingToDo"));
      }

      for (const problem of result.problems ?? []) toast.danger(problem);

      await load();
    } finally {
      setAcquiring(false);
    }
  };

  const saveRule = async () => {
    await BApi.collection.putCollection(collection.id, {
      ...collection,
      ruleSearchJson: ruleDraft ?? undefined,
    });
    toast.success(t<string>("collection.saved"));
    setRuleDraft(undefined);
    await load();
  };

  return (
    <div className="flex flex-col gap-3 p-2">
      <div className="flex items-center gap-2">
        <Button isIconOnly size="sm" variant="light" onPress={() => navigate("/collections")}>
          <AiOutlineArrowLeft className="text-base" />
        </Button>
        <span className="text-lg font-medium" style={{ color: collection.color ?? undefined }}>
          {collection.name}
        </span>
        <CatalogOwnerMark owner={owner} />
        <ProgressChip collection={collection} />
        <HelpCenterButton topic="collection" />
        <Tooltip content={t<string>("collection.action.openInResourcePage")}>
          <Button isIconOnly size="sm" variant="light" onPress={openInResourcePage}>
            <AiOutlineSearch className="text-base" />
          </Button>
        </Tooltip>
        <Button
          className="ml-auto"
          color="primary"
          isDisabled={readOnly || (collection.progress?.total ?? 0) === 0}
          isLoading={acquiring}
          size="sm"
          startContent={<AiOutlineCloudDownload className="text-base" />}
          variant="flat"
          onPress={acquireMissing}
        >
          {t<string>("collection.action.acquireMissing")}
        </Button>
      </div>

      {readOnly && (
        <p className="text-sm text-warning" role="status">
          {t("management.catalog.ownerUnavailable")}
        </p>
      )}
      <CompositionBar collection={collection} />

      <Tabs
        isVertical
        aria-label={collection.name}
        classNames={{
          tabWrapper: "w-full min-w-0 items-start gap-2 sm:gap-4",
          base: "w-24 shrink-0 sm:w-32",
          tabList: "w-full",
          tab: "justify-start px-2 sm:px-3",
          tabContent: "whitespace-normal text-left",
          panel: "min-w-0 flex-1 overflow-x-auto px-0",
        }}
      >
        <Tab key="members" title={t<string>("collection.tab.members")}>
          {readOnly ? (
            <Button size="sm" variant="flat" onPress={openInResourcePage}>
              {t("collection.action.openInResourcePage")}
            </Button>
          ) : (
            <MembersTab collection={collection} onChanged={load} />
          )}
        </Tab>
        <Tab key="source" title={t<string>("collection.tab.source")}>
          <div className="py-2">
            {!readOnly && <SourceTab collection={collection} onChanged={load} />}
          </div>
        </Tab>
        <Tab key="rule" title={t<string>("collection.tab.rule")}>
          <fieldset className="py-2 flex flex-col gap-3" disabled={readOnly}>
            <CollectionRuleEditor value={collection.ruleSearchJson} onChange={setRuleDraft} />
            <div>
              {/* Saving is a decision. A rule that saved itself on every keystroke would keep
                  rewriting who is in the collection while somebody is still choosing. */}
              <Button
                color="primary"
                isDisabled={readOnly || ruleDraft === undefined}
                size="sm"
                onPress={saveRule}
              >
                {t<string>("collection.action.saveRule")}
              </Button>
            </div>
          </fieldset>
        </Tab>
        <Tab key="settings" title={t<string>("collection.tab.settings")}>
          <div className="py-2">
            <fieldset className="min-w-0" disabled={readOnly}>
              <SettingsTab
                collection={collection}
                configurationAvailable={!readOnly}
                onSaved={load}
              />
            </fieldset>
          </div>
        </Tab>
      </Tabs>
    </div>
  );
};

const CollectionDetailPage = () => {
  const [params] = useSearchParams();
  const serverId = params.get("serverId"),
    libraryEpoch = params.get("libraryEpoch");

  return serverId ? (
    <OwnedCollectionDetail libraryEpoch={libraryEpoch} serverId={serverId} />
  ) : (
    <CollectionDetailContent />
  );
};
const OwnedCollectionDetail = ({
  serverId,
  libraryEpoch,
}: {
  serverId: string;
  libraryEpoch: string | null;
}) => {
  const { sources, loading } = useManagementSources();
  const { t } = useTranslation();
  const source = sources.find(
    (s) => s.serverId === serverId && s.libraryEpoch === libraryEpoch && !s.metadata?.denied,
  );

  if (loading) return <Spinner />;
  if (!source || !canReadSourceMetadata(source))
    return (
      <p className="text-warning" role="alert">
        {t("management.catalog.unavailable", { device: source?.name ?? serverId })}
      </p>
    );

  return (
    <ResourceOwnerProvider key={`${serverId}:${libraryEpoch}`} source={source}>
      <CollectionDetailContent />
    </ResourceOwnerProvider>
  );
};

CollectionDetailPage.displayName = "CollectionDetailPage";

export default CollectionDetailPage;
