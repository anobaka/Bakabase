import { useTranslation } from "react-i18next";
import {
  AiOutlineCloudDownload,
  AiOutlineDelete,
  AiOutlineDownload,
  AiOutlineMore,
} from "react-icons/ai";
import { Button, Dropdown, DropdownTrigger, DropdownMenu, DropdownItem } from "@/components/bakaui";

export default function FilteredTaskActions({
  count,
  filtered,
  disabled,
  loading,
  onDelete,
  onExportInstructions,
  onExportExcel,
  onCatchUp,
}: {
  count?: number;
  filtered: boolean;
  disabled: boolean;
  loading?: boolean;
  onDelete: () => void;
  onExportInstructions: () => void;
  onExportExcel: () => void;
  onCatchUp: () => void;
}) {
  const { t } = useTranslation();
  return (
    <Dropdown>
      <DropdownTrigger>
        <Button
          size="sm"
          variant="flat"
          startContent={<AiOutlineMore aria-hidden />}
          isDisabled={disabled || !count}
          isLoading={loading}
        >
          {count == null
            ? t("postParser.actions.unknown")
            : t(filtered ? "postParser.actions.filtered" : "postParser.actions.all", { count })}
        </Button>
      </DropdownTrigger>
      <DropdownMenu
        aria-label={t("postParser.actions.menu")}
        disabledKeys={disabled ? ["acquire", "json", "excel", "delete"] : []}
        onAction={(key) => {
          if (disabled || !count) return;
          if (key === "delete") onDelete();
          else if (key === "json") onExportInstructions();
          else if (key === "excel") onExportExcel();
          else if (key === "acquire") onCatchUp();
        }}
      >
        <DropdownItem
          key="acquire"
          aria-label={t("postParser.batchCatchUp.action")}
          startContent={<AiOutlineCloudDownload aria-hidden />}
          description={t("postParser.batchCatchUp.actionHint")}
          showDivider
        >
          {t("postParser.batchCatchUp.action")}
        </DropdownItem>
        <DropdownItem
          key="json"
          aria-label={t("postParser.actions.exportInstructions")}
          startContent={<AiOutlineDownload aria-hidden />}
        >
          {t("postParser.actions.exportInstructions")}
        </DropdownItem>
        <DropdownItem
          key="excel"
          aria-label={t("postParser.actions.exportExcel")}
          startContent={<AiOutlineDownload aria-hidden />}
          showDivider
        >
          {t("postParser.actions.exportExcel")}
        </DropdownItem>
        <DropdownItem
          key="delete"
          aria-label={t(
            filtered ? "postParser.action.deleteFiltered" : "postParser.action.deleteAllCount",
            { count },
          )}
          color="danger"
          className="text-danger"
          startContent={<AiOutlineDelete aria-hidden />}
          description={t("postParser.actions.deleteHint")}
        >
          {t(filtered ? "postParser.action.deleteFiltered" : "postParser.action.deleteAllCount", {
            count,
          })}
        </DropdownItem>
      </DropdownMenu>
    </Dropdown>
  );
}
