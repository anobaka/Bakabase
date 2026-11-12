import { useTranslation } from "react-i18next";
import { Button } from "@/components/bakaui";

export default function DetailExpansionControls({
  onChange,
  currentPage = false,
}: {
  onChange: (expanded: boolean) => void;
  currentPage?: boolean;
}) {
  const { t } = useTranslation();
  return (
    <div
      className="flex shrink-0 items-center gap-1"
      role="group"
      aria-label={t("workflow.details.expansion")}
    >
      <Button size="sm" variant="light" onPress={() => onChange(true)}>
        {t(currentPage ? "workflow.details.expandPage" : "workflow.details.expandAll")}
      </Button>
      <Button size="sm" variant="light" onPress={() => onChange(false)}>
        {t(currentPage ? "workflow.details.collapsePage" : "workflow.details.collapseAll")}
      </Button>
    </div>
  );
}
