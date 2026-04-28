import { useEffect, useMemo, useState } from "react";
import { useTranslation } from "react-i18next";
import toast from "react-hot-toast";
import { AiOutlineRadarChart } from "react-icons/ai";
import {
  ClearOutlined,
  CopyOutlined,
  DeleteOutlined,
  EditOutlined,
  PlusOutlined,
} from "@ant-design/icons";

import { HealthScoreProfileEditor } from "./components/HealthScoreProfileEditor";
import { ScoreNowButton } from "./components/ScoreNowButton";
import ConfirmModal from "./components/ConfirmModal";

import { Button, Chip, Spinner, Switch, Tooltip } from "@/components/bakaui";
import {
  ResourceFilterController,
} from "@/components/ResourceFilter";
import type { SearchFilterGroup } from "@/components/ResourceFilter/models";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import BApi from "@/sdk/BApi";
import {
  HealthScoreProfile,
  apiFilterGroupToInternal,
} from "@/pages/health-score/types";

const isEmptyFilter = (g: SearchFilterGroup | null | undefined): boolean => {
  if (!g) return true;
  const filters = (g.filters ?? []).length;
  const groups = (g.groups ?? []).length;
  return filters === 0 && groups === 0;
};

const HealthScorePage = () => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const [profiles, setProfiles] = useState<HealthScoreProfile[]>();
  const [editing, setEditing] = useState<HealthScoreProfile | null>(null);

  const load = async () => {
    const r = await BApi.healthScore.getAllHealthScoreProfiles();
    setProfiles(r.data ?? []);
  };

  useEffect(() => {
    load();
  }, []);

  const handleAdd = async () => {
    const r = await BApi.healthScore.addHealthScoreProfile();
    if (r.data) setEditing(r.data);
    await load();
  };

  const handleDelete = (id: number) => {
    createPortal(ConfirmModal, {
      title: t<string>("healthScore.action.delete"),
      message: t<string>("healthScore.confirm.delete"),
      confirmLabel: t<string>("common.action.delete"),
      destructive: true,
      onConfirm: async () => {
        await BApi.healthScore.deleteHealthScoreProfile(id);
        await load();
      },
    });
  };

  const handleDuplicate = async (id: number) => {
    await BApi.healthScore.duplicateHealthScoreProfile(id);
    await load();
  };

  const handleToggleEnabled = async (p: HealthScoreProfile, enabled: boolean) => {
    await BApi.healthScore.patchHealthScoreProfile(p.id, { enabled });
    await load();
  };

  const handleClearCache = async (id: number) => {
    await BApi.healthScore.clearHealthScoreProfileCache(id);
    toast.success(t<string>("healthScore.toast.cacheCleared"));
  };

  const handleClearAll = () => {
    createPortal(ConfirmModal, {
      title: t<string>("healthScore.action.clearAllCaches"),
      message: t<string>("healthScore.confirm.clearAllCaches"),
      confirmLabel: t<string>("healthScore.action.clearAllCaches"),
      destructive: true,
      onConfirm: async () => {
        await BApi.healthScore.clearAllHealthScoreCaches();
        toast.success(t<string>("healthScore.toast.cacheCleared"));
      },
    });
  };

  if (!profiles) return <Spinner />;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold">{t<string>("healthScore.title")}</h1>
          <p className="text-sm text-default-500">{t<string>("healthScore.description")}</p>
        </div>
        <div className="flex items-center gap-2">
          <ScoreNowButton />
          <Button
            color="warning"
            startContent={<ClearOutlined className="text-lg" />}
            variant="flat"
            onPress={handleClearAll}
          >
            {t<string>("healthScore.action.clearAllCaches")}
          </Button>
          <Button
            color="primary"
            startContent={<PlusOutlined className="text-lg" />}
            onPress={handleAdd}
          >
            {t<string>("healthScore.action.add")}
          </Button>
        </div>
      </div>

      {profiles.length === 0 ? (
        <div className="flex flex-col items-center justify-center min-h-[300px] gap-3">
          <AiOutlineRadarChart className="text-6xl text-default-300" />
          <p className="text-default-500">{t<string>("healthScore.empty.noProfiles")}</p>
          <Button
            color="primary"
            startContent={<PlusOutlined className="text-lg" />}
            onPress={handleAdd}
          >
            {t<string>("healthScore.action.add")}
          </Button>
        </div>
      ) : (
        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          {profiles.map((p) => (
            <ProfileCard
              key={p.id}
              profile={p}
              onEdit={() => setEditing(p)}
              onDelete={() => handleDelete(p.id)}
              onDuplicate={() => handleDuplicate(p.id)}
              onClearCache={() => handleClearCache(p.id)}
              onToggleEnabled={(v) => handleToggleEnabled(p, v)}
            />
          ))}
        </div>
      )}

      {editing && (
        <HealthScoreProfileEditor
          profile={editing}
          onClose={() => setEditing(null)}
          onSaved={load}
        />
      )}
    </div>
  );
};

interface ProfileCardProps {
  profile: HealthScoreProfile;
  onEdit: () => void;
  onDelete: () => void;
  onDuplicate: () => void;
  onClearCache: () => void;
  onToggleEnabled: (v: boolean) => void;
}

const ProfileCard = ({
  profile: p,
  onEdit,
  onDelete,
  onDuplicate,
  onClearCache,
  onToggleEnabled,
}: ProfileCardProps) => {
  const { t } = useTranslation();
  // Memoized so the inner Filter components keep a stable propsFilter reference.
  // Without this, Filter's useUpdateEffect resets local state on every parent
  // render and discards the valueProperty it lazily fetched — leaving the value
  // cell blank with only property + operation visible.
  const filter = useMemo(
    () => apiFilterGroupToInternal(p.membershipFilter),
    [p.membershipFilter],
  );
  const filterIsEmpty = isEmptyFilter(filter);
  const rules = p.rules ?? [];

  return (
    <div className="border border-default-200 rounded-lg p-3 flex flex-col gap-2">
      <div className="flex items-center justify-between">
        <div className="font-medium">{p.name}</div>
        <Switch
          isSelected={p.enabled ?? false}
          size="sm"
          onValueChange={onToggleEnabled}
        />
      </div>
      <div className="flex flex-wrap items-center gap-2 text-xs text-default-500">
        <Chip size="sm" variant="flat">
          {t<string>("healthScore.label.priority")}: {p.priority ?? 0}
        </Chip>
        <Chip size="sm" variant="flat">
          {t<string>("healthScore.label.baseScore")}: {p.baseScore ?? 100}
        </Chip>
        <Chip size="sm" variant="flat">
          {t<string>("healthScore.label.lastMatched")}: {p.lastMatchedResourceCount ?? "—"}
        </Chip>
      </div>

      <div className="flex flex-col gap-1">
        <div className="text-xs font-medium text-default-600">
          {t<string>("healthScore.tab.resources")}
        </div>
        {filterIsEmpty ? (
          <div className="text-xs text-default-500">
            {t<string>("healthScore.empty.noFilter")}
          </div>
        ) : (
          <ResourceFilterController
            isReadonly
            group={filter}
            onGroupChange={() => {}}
            filterLayout="vertical"
          />
        )}
      </div>

      <div className="flex flex-col gap-1">
        <div className="text-xs font-medium text-default-600">
          {t<string>("healthScore.tab.rules")} ({rules.length})
        </div>
        {rules.length === 0 ? (
          <div className="text-xs text-default-500">
            {t<string>("healthScore.empty.noRules")}
          </div>
        ) : (
          <div className="flex flex-wrap gap-1">
            {rules.map((r, i) => {
              const delta = r.delta ?? 0;
              const color = delta > 0 ? "success" : delta < 0 ? "danger" : "default";
              return (
                <Chip key={i} color={color} size="sm" variant="flat">
                  {r.name || `#${i + 1}`} ({delta > 0 ? "+" : ""}{delta})
                </Chip>
              );
            })}
          </div>
        )}
      </div>

      <div className="flex items-center justify-between gap-1">
        <span className="text-xs text-default-400">
          {p.createdAt ? new Date(p.createdAt).toLocaleString() : ""}
        </span>
        <div className="flex gap-1">
          <Tooltip content={t<string>("healthScore.action.edit")}>
            <Button isIconOnly size="sm" variant="light" onPress={onEdit}>
              <EditOutlined className="text-lg" />
            </Button>
          </Tooltip>
          <Tooltip content={t<string>("healthScore.action.duplicate")}>
            <Button isIconOnly size="sm" variant="light" onPress={onDuplicate}>
              <CopyOutlined className="text-lg" />
            </Button>
          </Tooltip>
          <Tooltip content={t<string>("healthScore.action.clearCache")}>
            <Button isIconOnly size="sm" variant="light" onPress={onClearCache}>
              <ClearOutlined className="text-lg" />
            </Button>
          </Tooltip>
          <Tooltip content={t<string>("healthScore.action.delete")}>
            <Button
              isIconOnly
              color="danger"
              size="sm"
              variant="light"
              onPress={onDelete}
            >
              <DeleteOutlined className="text-lg" />
            </Button>
          </Tooltip>
        </div>
      </div>
    </div>
  );
};

export default HealthScorePage;
