import type { PropsWithChildren } from "react";
import type { ReferenceProperty } from "@/components/Property/referenceValues";

import { createContext, useContext, useMemo } from "react";
import { useTranslation } from "react-i18next";
import { SearchOutlined } from "@ant-design/icons";

import ReferenceValueResourcesModal from "./ReferenceValueResourcesModal";

import { Button } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useReferenceValueResourceCounts } from "@/hooks/useReferenceValueResourceCounts";

const UsageContext = createContext<
  | ({ property: ReferenceProperty; persistedValues: Set<string> } & ReturnType<
      typeof useReferenceValueResourceCounts
    >)
  | undefined
>(undefined);

export function ReferenceValueUsageProvider({
  property,
  options,
  children,
}: PropsWithChildren<{
  property?: ReferenceProperty;
  options?: {
    choices?: { value: string }[];
    tags?: { value: string }[];
    data?: { value: string; children?: any[] }[];
  };
}>) {
  const usage = useReferenceValueResourceCounts(property);
  const { t } = useTranslation();
  // Snapshot the saved IDs on open (or after a backend type conversion). Draft
  // options stay unsearchable, while saved values remain searchable during index warming.
  const persistedValues = useMemo(() => {
    const values = new Set(
      [...(options?.choices ?? []), ...(options?.tags ?? [])].map((value) => value.value),
    );
    const visit = (nodes: { value: string; children?: any[] }[]) =>
      nodes.forEach((node) => {
        values.add(node.value);
        visit(node.children ?? []);
      });

    visit(options?.data ?? []);

    return values;
  }, [property?.id, property?.type]);

  return (
    <UsageContext.Provider value={property ? { property, persistedValues, ...usage } : undefined}>
      {children}
      {property && (
        <div className="flex flex-wrap items-center gap-2 text-xs leading-relaxed text-default-500">
          <span>
            {t(
              usage.loading ? "property.reference.loadingCounts" : "property.reference.countsHelp",
            )}
          </span>
          {usage.error && (
            <Button size="sm" variant="light" onPress={usage.refresh}>
              {t("property.reference.retry")}
            </Button>
          )}
        </div>
      )}
    </UsageContext.Provider>
  );
}

export default function ReferenceValueUsage({ value, label }: { value: string; label?: string }) {
  const usage = useContext(UsageContext);
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();

  if (!usage) return null;
  const count = usage.counts?.[value];
  const exists = usage.persistedValues.has(value);

  return (
    <div className="flex shrink-0 items-center gap-0.5">
      {count !== 0 && (
        <span
          className="min-w-5 rounded-md bg-default-100 px-1.5 py-0.5 text-center text-xs text-default-500 tabular-nums"
          title={
            count === undefined
              ? t("property.reference.countUnavailable")
              : t("property.reference.resourceCount", { count })
          }
        >
          {count?.toLocaleString() ?? "—"}
        </span>
      )}
      <Button
        isIconOnly
        aria-label={t("property.reference.searchResources")}
        isDisabled={!exists || count === 0}
        size="sm"
        title={t(
          !exists
            ? "property.reference.saveBeforeSearch"
            : count === 0
              ? "property.reference.noResources"
              : "property.reference.searchResources",
        )}
        variant="light"
        onPress={() =>
          createPortal(ReferenceValueResourcesModal, {
            property: usage.property,
            value,
            label,
            onDestroyed: usage.refresh,
          })
        }
      >
        <SearchOutlined />
      </Button>
    </div>
  );
}
