"use client";

import type { CollectionModel } from "@/stores/collections";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { AiOutlineArrowLeft, AiOutlineCloudDownload, AiOutlineSearch } from "react-icons/ai";

import CompositionBar from "../components/CompositionBar";
import { buildCollectionSearch, percent } from "../helpers";

import MembersTab from "./components/MembersTab";
import SettingsTab from "./components/SettingsTab";
import SourceTab from "./components/SourceTab";

import BApi from "@/sdk/BApi";
import CollectionRuleEditor from "@/components/CollectionRuleEditor";
import { Button, Chip, Spinner, Tab, Tabs, Tooltip, toast } from "@/components/bakaui";
import { usePendingSearchStore } from "@/stores/pendingSearch";

/**
 * One collection: who is in it, where they came from, what rule keeps it, and what it does.
 *
 * The four tabs are the four questions a collection raises, in the order people ask them. Source
 * is a placeholder until subscriptions land — showing where a member came from only means
 * something once something other than a person can put one there.
 */
const CollectionDetailPage = () => {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const [params] = useSearchParams();
  const setPendingSearch = usePendingSearchStore((s) => s.setPendingSearch);

  const id = Number(params.get("id"));
  const [collection, setCollection] = useState<CollectionModel>();
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
      const rsp = await BApi.collection.getCollection(id);

      setCollection((rsp.data ?? undefined) as CollectionModel | undefined);
    } finally {
      setLoading(false);
    }
  }, [id]);

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
        <span className="text-default-400">{t<string>("collection.notFound")}</span>
        <Button size="sm" variant="light" onPress={() => navigate("/collections")}>
          {t<string>("collection.action.backToList")}
        </Button>
      </div>
    );
  }

  const openInResourcePage = () => {
    setPendingSearch(buildCollectionSearch(collection) as any);

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
        <Chip color={percent(collection) === 100 ? "success" : "default"} size="sm" variant="flat">
          {t<string>("collection.percentComplete", { percent: percent(collection) })}
        </Chip>
        <Tooltip content={t<string>("collection.action.openInResourcePage")}>
          <Button isIconOnly size="sm" variant="light" onPress={openInResourcePage}>
            <AiOutlineSearch className="text-base" />
          </Button>
        </Tooltip>
        <Button
          className="ml-auto"
          color="primary"
          isDisabled={(collection.progress?.total ?? 0) === 0}
          isLoading={acquiring}
          size="sm"
          startContent={<AiOutlineCloudDownload className="text-base" />}
          variant="flat"
          onPress={acquireMissing}
        >
          {t<string>("collection.action.acquireMissing")}
        </Button>
      </div>

      <CompositionBar collection={collection} />

      <Tabs>
        <Tab key="members" title={t<string>("collection.tab.members")}>
          <MembersTab collection={collection} onChanged={load} />
        </Tab>
        <Tab key="source" title={t<string>("collection.tab.source")}>
          <div className="py-2">
            <SourceTab collection={collection} onChanged={load} />
          </div>
        </Tab>
        <Tab key="rule" title={t<string>("collection.tab.rule")}>
          <div className="py-2 flex flex-col gap-3">
            <CollectionRuleEditor value={collection.ruleSearchJson} onChange={setRuleDraft} />
            <div>
              {/* Saving is a decision. A rule that saved itself on every keystroke would keep
                  rewriting who is in the collection while somebody is still choosing. */}
              <Button
                color="primary"
                isDisabled={ruleDraft === undefined}
                size="sm"
                onPress={saveRule}
              >
                {t<string>("collection.action.saveRule")}
              </Button>
            </div>
          </div>
        </Tab>
        <Tab key="settings" title={t<string>("collection.tab.settings")}>
          <div className="py-2">
            <SettingsTab collection={collection} onSaved={load} />
          </div>
        </Tab>
      </Tabs>
    </div>
  );
};

CollectionDetailPage.displayName = "CollectionDetailPage";

export default CollectionDetailPage;
