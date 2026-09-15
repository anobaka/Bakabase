import type { IProperty } from "@/components/Property/models";

import { useTranslation } from "react-i18next";

import { AttachmentLayout, PropertyType } from "@/sdk/constants";
import { Chip } from "@/components/bakaui";

type SummaryValue = { value: string; label?: string; color?: string };

const PropertySummary = ({ property }: { property: IProperty }) => {
  const { t } = useTranslation();
  const options = property.options ?? {};
  let values: SummaryValue[] | undefined;
  let summary: string | undefined;

  switch (property.type) {
    case PropertyType.SingleChoice:
    case PropertyType.MultipleChoice:
      values = options.choices ?? [];
      break;
    case PropertyType.Tags:
      values = (options.tags ?? []).map(
        (tag: { value: string; group?: string; name?: string; color?: string }) => ({
          value: tag.value,
          label: tag.group ? `${tag.group}:${tag.name ?? ""}` : tag.name,
          color: tag.color,
        }),
      );
      break;
    case PropertyType.Number:
      summary = t("customProperty.summary.precision", { count: options.precision ?? 0 });
      break;
    case PropertyType.Percentage:
      summary = t("customProperty.summary.precision", { count: options.precision ?? 0 });
      if (options.showProgressBar ?? options.showProgressbar)
        summary += ` · ${t("customProperty.summary.progress")}`;
      break;
    case PropertyType.Rating:
      summary = t("customProperty.summary.rating", { max: options.maxValue ?? 5 });
      break;
    case PropertyType.Multilevel:
      summary = t("customProperty.summary.rootNodes", { count: options.data?.length ?? 0 });
      break;
    case PropertyType.Attachment:
      summary = t(
        options.layout === AttachmentLayout.Carousel
          ? "property.attachment.layout.carousel"
          : "property.attachment.layout.tile",
      );
      break;
  }
  if (values) {
    if (values.length === 0)
      return (
        <span className="text-xs text-default-400">{t("customProperty.summary.noOptions")}</span>
      );

    return (
      <div className="flex min-w-0 flex-wrap items-center gap-1.5">
        {values.slice(0, 3).map((value) => (
          <Chip
            key={value.value}
            className="min-w-0 max-w-full"
            classNames={{ content: "min-w-0 truncate" }}
            size="sm"
            style={
              value.color
                ? {
                    color: value.color,
                    backgroundColor: `color-mix(in srgb, ${value.color} 15%, transparent)`,
                  }
                : undefined
            }
            variant="flat"
          >
            {value.label}
          </Chip>
        ))}
        {values.length > 3 && (
          <span className="text-xs text-default-400">+{values.length - 3}</span>
        )}
      </div>
    );
  }

  return (
    <span className="text-xs leading-relaxed text-default-500">
      {summary ?? t(`property.editor.typeDescription.${PropertyType[property.type]}`)}
    </span>
  );
};

export default PropertySummary;
