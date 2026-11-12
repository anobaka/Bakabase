import { useTranslation } from "react-i18next";
import { Chip, Tooltip } from "@/components/bakaui";

export default function AcquisitionAttentionTags({ tags }: { tags?: string[] | null }) {
  const { t } = useTranslation();
  if (!tags?.length) return null;
  return (
    <div className="flex flex-wrap gap-1" aria-label={t("acquisition.attention.title")}>
      {tags.map((tag) => (
        <Tooltip
          key={tag}
          content={t(`acquisition.attention.hint.${tag}`)}
          delay={1000}
          placement="bottom"
        >
          <Chip size="sm" variant="flat" color={tag === "sourceUnavailable" ? "danger" : "warning"}>
            {t(`acquisition.attention.tag.${tag}`)}
          </Chip>
        </Tooltip>
      ))}
    </div>
  );
}
