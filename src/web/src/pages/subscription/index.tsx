"use client";

import type { components } from "@/sdk/BApi2";

import React, { useCallback, useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  DeleteOutlined,
  EditOutlined,
  PlusCircleOutlined,
  ReloadOutlined,
} from "@ant-design/icons";

import SubscriptionEditor from "@/components/Subscription/SubscriptionEditor";
import { getProviderUI } from "@/components/Subscription/Providers";
import ThirdPartyLabel from "@/components/ThirdPartyLabel";
import BApi from "@/sdk/BApi";
import { Button, Chip, Modal, Spinner, Switch, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";

type SubscriptionVm =
  components["schemas"]["Bakabase.Modules.Subscription.Abstractions.Models.View.SubscriptionViewModel"];
type ProviderVm =
  components["schemas"]["Bakabase.Modules.Subscription.Abstractions.Models.View.SubscriptionProviderViewModel"];

function formatTime(iso?: string | null): string | null {
  if (!iso) return null;
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

const SubscriptionPage: React.FC = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  const [subscriptions, setSubscriptions] = useState<SubscriptionVm[]>([]);
  const [providers, setProviders] = useState<ProviderVm[]>([]);
  const [loading, setLoading] = useState(true);
  // Track per-row in-flight "run now" so the spinner stays put without blocking other actions.
  const [running, setRunning] = useState<Record<number, boolean>>({});

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [subRsp, provRsp] = await Promise.all([
        BApi.subscription.searchSubscriptions({}),
        BApi.subscription.getSubscriptionProviders(),
      ]);
      setSubscriptions((subRsp.data ?? []) as SubscriptionVm[]);
      setProviders((provRsp.data ?? []) as ProviderVm[]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const handleAdd = () => {
    createPortal(SubscriptionEditor, {
      providers,
      onSaved: load,
    });
  };

  const handleEdit = (sub: SubscriptionVm) => {
    createPortal(SubscriptionEditor, {
      subscription: sub,
      providers,
      onSaved: load,
    });
  };

  const handleDelete = (sub: SubscriptionVm) => {
    createPortal(Modal, {
      defaultVisible: true,
      title: t<string>("subscription.action.delete.confirm"),
      children: sub.displayName,
      onOk: async () => {
        await BApi.subscription.deleteSubscription(sub.id);
        await load();
      },
    });
  };

  const handleRunNow = async (sub: SubscriptionVm) => {
    if (running[sub.id]) return;
    setRunning((r) => ({ ...r, [sub.id]: true }));
    try {
      // Server returns a summary: first-run flag, new/updated counts, error. The previous
      // `toast.success(sub.displayName)` echoed only the name and gave no hint whether
      // anything changed — confusing especially on the very first run, where downstream
      // notifications + workflow triggers are intentionally suppressed.
      const rsp = await BApi.subscription.runSubscriptionCheck(sub.id);
      const summary = rsp.data;
      if (!summary) {
        toast.warning(t<string>("subscription.runNow.notFound", { name: sub.displayName }));
      } else if (summary.error) {
        toast.danger({
          title: t<string>("subscription.runNow.error", { name: sub.displayName }),
          description: summary.error,
        });
      } else if (summary.firstRun) {
        toast.success({
          title: t<string>("subscription.runNow.firstRun", { name: sub.displayName }),
          description: t<string>("subscription.runNow.firstRun.description"),
        });
      } else if ((summary.newItemCount ?? 0) === 0 && (summary.updatedItemCount ?? 0) === 0) {
        toast.default(t<string>("subscription.runNow.noChanges", { name: sub.displayName }));
      } else {
        toast.success({
          title: t<string>("subscription.runNow.changed", { name: sub.displayName }),
          description: t<string>("subscription.runNow.changed.description", {
            newCount: summary.newItemCount ?? 0,
            updatedCount: summary.updatedItemCount ?? 0,
          }),
        });
      }
      await load();
    } catch (e: any) {
      toast.danger(e?.message ?? String(e));
    } finally {
      setRunning((r) => ({ ...r, [sub.id]: false }));
    }
  };

  const handleToggleEnabled = async (sub: SubscriptionVm, next: boolean) => {
    await BApi.subscription.patchSubscription(sub.id, { enabled: next });
    setSubscriptions((s) =>
      s.map((x) => (x.id === sub.id ? { ...x, enabled: next } : x)),
    );
  };

  return (
    <div className="flex flex-col gap-3 p-4">
      <div className="flex items-center gap-2">
        <h2 className="text-lg font-semibold">{t<string>("subscription.title")}</h2>
        <Button
          color="primary"
          size="sm"
          startContent={<PlusCircleOutlined />}
          onPress={handleAdd}
        >
          {t<string>("subscription.action.add")}
        </Button>
      </div>

      {loading ? (
        <div className="flex justify-center py-10">
          <Spinner size="lg" />
        </div>
      ) : subscriptions.length === 0 ? (
        <div className="text-center text-default-500 py-10">
          {t<string>("subscription.empty")}
        </div>
      ) : (
        <div className="flex flex-col gap-2">
          {subscriptions.map((sub) => {
            const providerUi = getProviderUI(sub.kind);
            const providerVm = providers.find((p) => p.kind === sub.kind);
            const targetObj = providerUi
              ? providerUi.parseTarget(sub.targetJson)
              : null;
            const SummaryComponent = providerUi?.Summary;

            return (
              <div
                key={sub.id}
                className="border border-default-200 rounded-lg p-3 flex items-center gap-3"
              >
                <Switch
                  isSelected={sub.enabled}
                  size="sm"
                  onValueChange={(v) => handleToggleEnabled(sub, v)}
                />

                <div className="flex-1 min-w-0 flex flex-col gap-1">
                  {/* Source + name + type all on the same line — Source label leads
                      so it works as a visual icon column, then the name (taking the
                      bulk of the width), then the kind chip on the right. */}
                  <div className="flex items-center gap-2 min-w-0">
                    {providerUi ? (
                      <ThirdPartyLabel thirdPartyId={providerUi.thirdPartyId} />
                    ) : (
                      <Chip color="warning" size="sm" variant="flat">
                        {sub.kind}
                      </Chip>
                    )}
                    <span className="font-medium truncate">{sub.displayName}</span>
                    <Chip color="default" size="sm" variant="flat">
                      {t<string>(`subscription.provider.${sub.kind}.subKindLabel`, {
                        defaultValue: providerVm?.displayName ?? sub.kind,
                      })}
                    </Chip>
                  </div>
                  {SummaryComponent && targetObj && (
                    <SummaryComponent target={targetObj} />
                  )}
                  <div className="text-xs text-default-400 flex flex-wrap gap-x-3 gap-y-1">
                    <span>
                      {t<string>("subscription.status.lastChecked")}:{" "}
                      {formatTime(sub.lastCheckedAt) ??
                        t<string>("subscription.status.lastCheckedNever")}
                    </span>
                    {sub.lastChangeAt && (
                      <span>
                        {t<string>("subscription.status.lastChange")}:{" "}
                        {formatTime(sub.lastChangeAt)}
                      </span>
                    )}
                  </div>
                  {sub.lastError && (
                    <div className="text-xs text-danger">
                      {t<string>("subscription.status.error")}: {sub.lastError}
                    </div>
                  )}
                </div>

                <div className="flex items-center gap-1">
                  <Button
                    isIconOnly
                    isLoading={running[sub.id]}
                    size="sm"
                    title={t<string>("subscription.action.runNow.tooltip")}
                    variant="light"
                    onPress={() => void handleRunNow(sub)}
                  >
                    <ReloadOutlined className="text-lg" />
                  </Button>
                  <Button
                    isIconOnly
                    size="sm"
                    variant="light"
                    onPress={() => handleEdit(sub)}
                  >
                    <EditOutlined className="text-lg" />
                  </Button>
                  <Button
                    isIconOnly
                    color="danger"
                    size="sm"
                    variant="light"
                    onPress={() => handleDelete(sub)}
                  >
                    <DeleteOutlined className="text-lg" />
                  </Button>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};

export default SubscriptionPage;
