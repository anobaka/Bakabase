import { useTranslation } from "react-i18next";
import { TbSortAscendingLetters } from "react-icons/tb";

import { useMediaLibraryOrder } from "./useMediaLibraryOrder";

import { Button } from "@/components/bakaui";

export type LibraryOrderEntry = { key: string; label: string };

/** Mounted independently in portals as well, so pending/error state never goes stale. */
export default function MediaLibraryOrderToolbar({
  entries,
  disabled = false,
}: {
  entries: LibraryOrderEntry[];
  disabled?: boolean;
}) {
  const { t } = useTranslation();
  const order = useMediaLibraryOrder(entries.map(({ key }) => key));

  return (
    <div className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-default-400">
      <span>{t("mediaLibrary.optionOrder.label")}</span>
      <Button
        className="h-6 min-w-0 gap-1 px-2 text-xs"
        isDisabled={order.saving || disabled || entries.length < 2}
        isLoading={order.saving}
        size="sm"
        startContent={<TbSortAscendingLetters aria-hidden size={14} />}
        variant="light"
        onPress={() => {
          const byName = [...entries].sort(
            (a, b) => a.label.localeCompare(b.label) || a.key.localeCompare(b.key),
          );

          void order.arrange(byName.map(({ key }) => key));
        }}
      >
        {t("mediaLibrary.optionOrder.name")}
      </Button>
      <span>{t("mediaLibrary.optionOrder.description")}</span>
      {order.error && (
        <span className="text-danger" role="alert">
          {t("mediaLibrary.optionOrder.saveFailed")}
        </span>
      )}
    </div>
  );
}
