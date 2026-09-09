"use client";

import type { CollectionModel } from "@/stores/collections";
import type { components } from "@/sdk/BApi2";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { AiOutlinePlusCircle, AiOutlineReload } from "react-icons/ai";

import BApi from "@/sdk/BApi";
import SubscriptionEditor from "@/components/Subscription/SubscriptionEditor";
import { Button, Chip, Spinner, Tooltip, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type SubscriptionVm =
  components["schemas"]["Bakabase.Modules.Subscription.Abstractions.Models.View.SubscriptionViewModel"];
type ProviderVm =
  components["schemas"]["Bakabase.Modules.Subscription.Abstractions.Models.View.SubscriptionProviderViewModel"];

type Props = {
  collection: CollectionModel;
  onChanged: () => void;
};

/**
 * Where this collection's members come from, other than the user.
 *
 * A collection with a source maintains itself: the source says what exists, and the collection
 * holds it whether or not the files are here yet. That is the difference between a list somebody
 * keeps up to date by hand and one that is simply true.
 */
const SourceTab: React.FC<Props> = ({ collection, onChanged }) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [subscriptions, setSubscriptions] = useState<SubscriptionVm[]>([]);
  const [providers, setProviders] = useState<ProviderVm[]>([]);
  const [loading, setLoading] = useState(true);
  const [checking, setChecking] = useState<number>();

  const load = useCallback(async () => {
    try {
      const [subsRsp, providersRsp] = await Promise.all([
        BApi.subscription.searchSubscriptions({}),
        BApi.subscription.getSubscriptionProviders(),
      ]);

      setSubscriptions(
        ((subsRsp.data ?? []) as SubscriptionVm[]).filter((s) => s.collectionId === collection.id),
      );
      setProviders((providersRsp.data ?? []) as ProviderVm[]);
    } finally {
      setLoading(false);
    }
  }, [collection.id]);

  useEffect(() => {
    void load();
  }, [load]);

  const edit = (subscription?: SubscriptionVm) =>
    createPortal(SubscriptionEditor, {
      subscription,
      providers,
      onSaved: async () => {
        await load();
        onChanged();
      },
    });

  const checkNow = async (subscription: SubscriptionVm) => {
    setChecking(subscription.id);
    try {
      const summary = (await BApi.subscription.runSubscriptionCheck(subscription.id)).data;

      if (summary?.error) {
        toast.danger(summary.error);
      } else if ((summary?.newItemCount ?? 0) > 0) {
        toast.success(t<string>("collection.source.foundCount", { count: summary!.newItemCount }));
      } else {
        toast.default(t<string>("collection.source.foundNothing"));
      }

      await load();
      onChanged();
    } finally {
      setChecking(undefined);
    }
  };

  if (loading) {
    return (
      <div className="flex justify-center py-10">
        <Spinner />
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center gap-2">
        <span className="text-sm text-default-500">
          {t<string>("collection.source.description")}
        </span>
        <Button
          className="ml-auto"
          size="sm"
          startContent={<AiOutlinePlusCircle className="text-base" />}
          variant="light"
          onPress={() => edit()}
        >
          {t<string>("collection.source.add")}
        </Button>
      </div>

      {subscriptions.length === 0 ? (
        <div className="flex flex-col gap-1 py-8 text-center text-default-400">
          <span>{t<string>("collection.source.none")}</span>
          <span className="text-xs">{t<string>("collection.source.noneHint")}</span>
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          {subscriptions.map((s) => (
            <div
              key={s.id}
              className="flex items-center gap-2 rounded-medium border border-divider px-3 py-2"
            >
              <div className="flex flex-col min-w-0">
                <span className="text-sm truncate">{s.displayName}</span>
                <span className="text-xs text-default-400 truncate">
                  {s.targetSummary || s.kind}
                </span>
              </div>

              {!s.enabled && (
                <Chip size="sm" variant="flat">
                  {t<string>("collection.source.paused")}
                </Chip>
              )}
              {s.lastError && (
                <Tooltip content={s.lastError}>
                  <Chip color="danger" size="sm" variant="flat">
                    {t<string>("collection.source.failing")}
                  </Chip>
                </Tooltip>
              )}

              <span className="ml-auto text-xs text-default-400">
                {s.lastCheckedAt
                  ? t<string>("collection.source.lastChecked", {
                      at: new Date(s.lastCheckedAt).toLocaleString(),
                    })
                  : t<string>("collection.source.neverChecked")}
              </span>

              <Tooltip content={t<string>("collection.source.checkNow")}>
                <Button
                  isIconOnly
                  isLoading={checking === s.id}
                  size="sm"
                  variant="light"
                  onPress={() => checkNow(s)}
                >
                  <AiOutlineReload className="text-base" />
                </Button>
              </Tooltip>
              <Button size="sm" variant="light" onPress={() => edit(s)}>
                {t<string>("collection.source.edit")}
              </Button>
            </div>
          ))}
        </div>
      )}
    </div>
  );
};

SourceTab.displayName = "SourceTab";

export default SourceTab;
