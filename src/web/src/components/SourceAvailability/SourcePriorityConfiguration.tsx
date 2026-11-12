import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
} from "@dnd-kit/core";
import {
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import {
  AiOutlineArrowDown,
  AiOutlineArrowUp,
  AiOutlineDownload,
  AiOutlineHolder,
} from "react-icons/ai";
import { Button, Spinner } from "@/components/bakaui";
import ThirdPartyIcon from "@/components/ThirdPartyIcon";
import { CloudDownloadSources, ThirdPartyId } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";
import type { SourceProvider } from "./models";
import { orderProviders } from "./models";
import { sourceAvailabilityApi } from "./api";
import { DownloadMethodStatusChip } from "@/components/CloudDownload/DownloadMethodAvailability";

function ProviderRow({
  provider,
  index,
  length,
  disabled,
  move,
}: {
  provider: SourceProvider;
  index: number;
  length: number;
  disabled: boolean;
  move: (from: number, to: number) => void;
}) {
  const { t } = useTranslation();
  const sortable = useSortable({ id: provider.id, disabled });
  const thirdPartyId =
    CloudDownloadSources.find((source) => source.providerId === provider.id)?.thirdPartyId ??
    (provider.id === "exHentai" ? ThirdPartyId.ExHentai : undefined);
  const name =
    provider.id === "oneDrive"
      ? t("sourceAvailability.provider.oneDrive")
      : thirdPartyId != null
        ? t(getEnumKey("ThirdPartyId", ThirdPartyId[thirdPartyId]))
        : t(`sourceAvailability.provider.${provider.id}`, { defaultValue: provider.id });
  return (
    <li
      ref={sortable.setNodeRef}
      style={{
        transform: CSS.Transform.toString(sortable.transform),
        transition: sortable.transition,
      }}
      className={`flex min-w-0 items-center gap-2 rounded-lg border border-default-200 bg-content1 px-2 py-1.5 ${sortable.isDragging ? "relative z-10 shadow-lg" : ""}`}
    >
      <Button
        {...sortable.attributes}
        {...sortable.listeners}
        ref={sortable.setActivatorNodeRef}
        isIconOnly
        size="sm"
        variant="light"
        isDisabled={disabled}
        className="touch-none text-default-400"
        aria-label={t("sourceAvailability.priority.drag", { name })}
      >
        <AiOutlineHolder />
      </Button>
      <span className="w-4 text-xs tabular-nums text-default-400">{index + 1}</span>
      {thirdPartyId != null ? (
        <ThirdPartyIcon thirdPartyId={thirdPartyId} />
      ) : (
        <AiOutlineDownload aria-hidden className="shrink-0" />
      )}
      <span className="min-w-0 flex-1 text-sm">{name}</span>
      <DownloadMethodStatusChip providerId={provider.id} />
      <div className="flex shrink-0 gap-0.5">
        <Button
          size="sm"
          variant="light"
          isIconOnly
          isDisabled={disabled || index === 0}
          aria-label={t("sourceAvailability.priority.moveUp", { name })}
          onPress={() => move(index, index - 1)}
        >
          <AiOutlineArrowUp />
        </Button>
        <Button
          size="sm"
          variant="light"
          isIconOnly
          isDisabled={disabled || index === length - 1}
          aria-label={t("sourceAvailability.priority.moveDown", { name })}
          onPress={() => move(index, index + 1)}
        >
          <AiOutlineArrowDown />
        </Button>
      </div>
    </li>
  );
}

/** Shared preference editor; changing it never replans already running tasks. */
export default function SourcePriorityConfiguration({
  onSavingChange,
}: { onSavingChange?: (saving: boolean) => void } = {}) {
  const { t } = useTranslation();
  const [providers, setProviders] = useState<SourceProvider[]>([]);
  const [savedOrder, setSavedOrder] = useState<string[]>([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string>();
  const [loaded, setLoaded] = useState(false);
  const lock = useRef(false);
  const requestId = useRef(0);
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );
  const load = useCallback(async () => {
    const request = ++requestId.current;
    setLoading(true);
    setError(undefined);
    try {
      const [options, catalog] = await Promise.all([
        sourceAvailabilityApi.options(),
        sourceAvailabilityApi.providerIds(),
      ]);
      if (request !== requestId.current) return;
      const next = orderProviders(catalog, options.providerPriority);
      setProviders(next);
      setSavedOrder(next.map((provider) => provider.id));
      setLoaded(true);
    } catch (failure) {
      if (request === requestId.current) setError(failure instanceof Error ? failure.message : "");
    } finally {
      if (request === requestId.current) setLoading(false);
    }
  }, []);
  useEffect(() => {
    void load();
    return () => {
      requestId.current++;
    };
  }, [load]);
  const move = (from: number, to: number) => {
    if (lock.current || to < 0 || to >= providers.length) return;
    setProviders((items) => arrayMove(items, from, to));
    setSaved(false);
  };
  const save = async () => {
    if (lock.current || !loaded) return;
    lock.current = true;
    setSaving(true);
    onSavingChange?.(true);
    setError(undefined);
    setSaved(false);
    try {
      const options = await sourceAvailabilityApi.saveOptions({
        providerPriority: providers.map((provider) => provider.id),
      });
      const next = orderProviders(
        providers.map((provider) => provider.id),
        options.providerPriority,
      );
      setProviders(next);
      setSavedOrder(next.map((provider) => provider.id));
      setSaved(true);
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : "");
    } finally {
      lock.current = false;
      setSaving(false);
      onSavingChange?.(false);
    }
  };
  const dirty = providers.map((provider) => provider.id).join("\n") !== savedOrder.join("\n");
  return (
    <section className="space-y-3" aria-label={t("sourceAvailability.priority.title")}>
      <h3 className="text-sm font-medium">{t("sourceAvailability.priority.title")}</h3>
      <p className="text-xs leading-relaxed text-default-500">
        {t("sourceAvailability.priority.description")}
      </p>
      {loading ? (
        <div className="flex justify-center py-6">
          <Spinner aria-label={t("sourceAvailability.loading")} />
        </div>
      ) : (
        <DndContext
          sensors={sensors}
          collisionDetection={closestCenter}
          onDragEnd={({ active, over }) => {
            if (over && active.id !== over.id)
              move(
                providers.findIndex((provider) => provider.id === active.id),
                providers.findIndex((provider) => provider.id === over.id),
              );
          }}
        >
          <SortableContext
            items={providers.map((provider) => provider.id)}
            strategy={verticalListSortingStrategy}
          >
            <ol className="space-y-1.5">
              {providers.map((provider, index) => (
                <ProviderRow
                  key={provider.id}
                  provider={provider}
                  index={index}
                  length={providers.length}
                  disabled={saving}
                  move={move}
                />
              ))}
            </ol>
          </SortableContext>
        </DndContext>
      )}
      <p className="text-xs leading-relaxed text-default-500">
        {t("sourceAvailability.priority.scopeHint")}
      </p>
      <div className="flex flex-wrap items-center gap-2">
        <Button
          size="sm"
          color="primary"
          isDisabled={!loaded || loading || !dirty}
          isLoading={saving}
          onPress={() => void save()}
        >
          {t("sourceAvailability.priority.save")}
        </Button>
        <Button
          size="sm"
          variant="light"
          isDisabled={!loaded || loading || saving}
          onPress={() => {
            const baidu = providers.findIndex((provider) => provider.id === "baidu");
            if (baidu > 0) move(baidu, 0);
          }}
        >
          {t("sourceAvailability.priority.baiduFirst")}
        </Button>
        {saved && (
          <span role="status" className="text-xs text-success">
            {t("sourceAvailability.priority.saved")}
          </span>
        )}
      </div>
      {error != null && (
        <div role="alert" className="flex flex-wrap items-center gap-2 text-xs text-danger">
          <span>
            {error
              ? t(`sourceAvailability.error.${error}`, { defaultValue: error })
              : t("sourceAvailability.loadFailed")}
          </span>
          {!loaded && (
            <Button size="sm" variant="light" onPress={() => void load()}>
              {t("common.action.retry")}
            </Button>
          )}
        </div>
      )}
    </section>
  );
}
