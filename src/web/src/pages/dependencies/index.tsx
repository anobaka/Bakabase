import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router-dom";
import { AiOutlineReload, AiOutlineSearch } from "react-icons/ai";

import { Button, Input, Select, Spinner } from "@/components/bakaui";
import DependencyCard from "@/components/Dependency/DependencyCard";
import { useDependencyChecks } from "@/components/Dependency/DependencyManager";
import {
  matchesDependency,
  dependencyDisplayName,
  summarizeDependencies,
  type DependencyFilter,
} from "@/components/Dependency/presentation";
import { useDependentComponentContextsStore } from "@/stores/dependentComponentContexts";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

const filters: DependencyFilter[] = ["all", "attention", "installed", "missing", "installing"];

export default function DependenciesPage({
  embedded = false,
  active = true,
}: {
  embedded?: boolean;
  active?: boolean;
}) {
  const { t } = useTranslation();
  const contexts = useDependentComponentContextsStore((state) => state.contexts);
  const serverName = useRemoteAccessStore((state) => state.serverName);
  const serverId = useRemoteAccessStore((state) => state.serverId);
  const { entries, checking, checkDependencies } = useDependencyChecks();
  const [params, setParams] = useSearchParams();
  const focusedId = params.get("component");
  const [query, setQuery] = useState(params.get("q") ?? "");
  const [filter, setFilter] = useState<DependencyFilter>("all");
  const root = useRef<HTMLDivElement>(null);
  const lastFocus = useRef<string>();
  const counts = summarizeDependencies(contexts, entries);
  const focused = contexts.find((context) => context.id === focusedId);
  const visible = contexts.filter(
    (context) =>
      context.id === focusedId || matchesDependency(context, query, filter, t, entries[context.id]),
  );
  const focusIsFiltered =
    focused && !matchesDependency(focused, query, filter, t, entries[focused.id]);
  useEffect(() => {
    if (!active || !focused) {
      lastFocus.current = undefined;
      return;
    }
    const focusKey = `${serverId ?? ""}:${focused.id}`;
    if (lastFocus.current === focusKey) return;
    const element = Array.from(
      root.current?.querySelectorAll<HTMLElement>("[data-component-id]") ?? [],
    ).find((card) => card.dataset.componentId === focused.id);
    element?.scrollIntoView?.({ block: "center", behavior: "smooth" });
    element?.focus({ preventScroll: true });
    lastFocus.current = focusKey;
  }, [focused?.id, serverId, active]);
  useEffect(() => {
    setFilter("all");
    lastFocus.current = undefined;
  }, [serverId]);
  useEffect(() => {
    setQuery(params.get("q") ?? "");
  }, [params.get("q")]);
  return (
    <div ref={root} className="@container mx-auto flex w-full max-w-[1440px] flex-col gap-5">
      <header className="space-y-2">
        {!embedded && <h1 className="text-xl font-semibold">{t("dependencies.title")}</h1>}
        <p className="text-sm text-default-500">{t("dependencies.description")}</p>
        <p className="rounded-lg bg-default-100/70 p-3 text-sm text-default-600">
          {t("dependencies.server", { name: serverName || t("dependencies.currentServer") })}
        </p>
      </header>
      <div
        className="flex flex-wrap items-center gap-x-5 gap-y-2 text-sm"
        aria-label={t("dependencies.summary")}
      >
        <span>{t("dependencies.count.installed", { count: counts.installed })}</span>
        <span>{t("dependencies.count.installing", { count: counts.installing })}</span>
        <span className={counts.attention ? "text-warning-600" : "text-default-500"}>
          {t("dependencies.count.attention", { count: counts.attention })}
        </span>
        <span className="text-xs text-default-500">{t("dependencies.optionalHint")}</span>
      </div>
      <div className="flex flex-wrap items-end gap-3">
        <Input
          className="min-w-48 flex-1"
          label={t("dependencies.search")}
          value={query}
          onValueChange={setQuery}
          isClearable
          startContent={<AiOutlineSearch aria-hidden />}
        />
        <Select
          className="w-full sm:w-48"
          label={t("dependencies.filter.label")}
          selectedKeys={[filter]}
          disallowEmptySelection
          dataSource={filters.map((value) => ({ value, label: t(`dependencies.filter.${value}`) }))}
          onSelectionChange={(keys) => setFilter(String(Array.from(keys)[0]) as DependencyFilter)}
        />
        <Button
          className="mb-0.5"
          variant="flat"
          isLoading={checking}
          isDisabled={checking || !contexts.some((context) => context.isAvailableOnCurrentPlatform)}
          startContent={!checking && <AiOutlineReload aria-hidden />}
          onPress={() =>
            void checkDependencies(
              contexts
                .filter((context) => context.isAvailableOnCurrentPlatform)
                .map((context) => context.id),
            )
          }
        >
          {t("dependencies.checkAll")}
        </Button>
      </div>
      {focusedId && contexts.length > 0 && !focused && (
        <p className="rounded-lg bg-warning-50 p-3 text-sm text-warning-700" role="status">
          {t("dependencies.unknownComponent", { id: focusedId })}
        </p>
      )}
      {focused && (
        <div
          className="flex flex-wrap items-center gap-2 rounded-lg bg-primary-50 p-3 text-sm"
          role="status"
        >
          <span>
            {t(focusIsFiltered ? "dependencies.focusOutsideFilter" : "dependencies.focused", {
              name: dependencyDisplayName(focused, t),
            })}
          </span>
          <Button
            size="sm"
            variant="light"
            onPress={() => {
              const next = new URLSearchParams(params);
              next.delete("component");
              setParams(next, { replace: true });
            }}
          >
            {t("dependencies.clearFocus")}
          </Button>
        </div>
      )}
      {!contexts.length ? (
        <div className="flex items-center gap-2 py-10 text-sm text-default-500">
          <Spinner size="sm" />
          {t("configuration.dependency.loadingComponents")}
        </div>
      ) : !visible.length ? (
        <p className="rounded-xl bg-default-50 p-8 text-center text-sm text-default-500">
          {t("dependencies.empty")}
        </p>
      ) : (
        <div
          className={`grid auto-rows-fr grid-cols-1 items-stretch gap-4 ${embedded ? "@min-[740px]:grid-cols-2" : "@min-[950px]:grid-cols-2"}`}
        >
          {visible.map((context) => (
            <DependencyCard key={context.id} context={context} focused={focusedId === context.id} />
          ))}
        </div>
      )}
    </div>
  );
}
