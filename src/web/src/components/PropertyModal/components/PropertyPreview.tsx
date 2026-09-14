import type { IProperty, TypedMultilevelPropertyOptions } from "@/components/Property/models";

import { useTranslation } from "react-i18next";
import { FiImage, FiChevronLeft, FiChevronRight } from "react-icons/fi";

import { Progress, Rating } from "@/components/bakaui";
import PropertyValueRenderer from "@/components/Property/components/PropertyValueRenderer";
import { getBizValueType, getDbValueType } from "@/components/Property/PropertySystem";
import { serializeStandardValue } from "@/components/StandardValue/helpers";
import { AttachmentLayout, PropertyPool, PropertyType } from "@/sdk/constants";

interface Props {
  name?: string;
  type: PropertyType;
  options?: any;
}

const firstLeaf = (nodes: TypedMultilevelPropertyOptions["data"]): string | undefined => {
  const node = nodes?.[0];

  return node?.children?.length ? firstLeaf(node.children) : node?.value;
};

/** Draft-only examples: reference previews use this form's options, never resource data. */
const PropertyPreview = ({ name, type, options }: Props) => {
  const { t } = useTranslation();
  const property: IProperty = {
    id: 0,
    name: name || t("property.editor.previewName"),
    type,
    options,
    pool: PropertyPool.Custom,
    dbValueType: getDbValueType(type),
    bizValueType: getBizValueType(type),
    typeName: "",
    poolName: "",
    order: 0,
  };

  const renderExample = () => {
    if (type === PropertyType.Number || type === PropertyType.Percentage) {
      const precision = Math.max(0, Math.min(4, options?.precision ?? 0));
      const value = Number(80).toFixed(precision);

      if (
        type === PropertyType.Percentage &&
        (options?.showProgressBar ?? options?.showProgressbar)
      ) {
        return <Progress aria-label={`${value}%`} label={`${value}%`} size="sm" value={80} />;
      }

      return (
        <span className="text-lg tabular-nums">
          {value}
          {type === PropertyType.Percentage ? "%" : ""}
        </span>
      );
    }
    if (type === PropertyType.Rating) {
      const max = Math.max(1, Math.min(10, options?.maxValue ?? 5));

      return (
        <div className="flex flex-col gap-2">
          <Rating disabled count={max} size="sm" value={Math.max(1, max - 1)} />
          <span className="text-xs text-default-500">
            {t("property.editor.ratingScale", { count: max })}
          </span>
        </div>
      );
    }
    if (type === PropertyType.Attachment) {
      const carousel = options?.layout === AttachmentLayout.Carousel;

      return (
        <div className="flex flex-col gap-2">
          <div
            aria-label={t("property.editor.attachmentPreview")}
            className="flex items-center gap-2"
          >
            {carousel && <FiChevronLeft className="shrink-0 text-default-400" />}
            {Array.from({ length: carousel ? 1 : 3 }, (_, index) => (
              <div
                key={index}
                className="flex min-w-0 flex-1 flex-col items-center gap-2 rounded-lg bg-default-200/50 px-2 py-5 text-default-500"
              >
                <FiImage className="text-2xl" />
                <span className="text-xs">{index + 1} / 3</span>
              </div>
            ))}
            {carousel && <FiChevronRight className="shrink-0 text-default-400" />}
          </div>
          <p className="text-xs text-default-500">{t("property.editor.attachmentHint")}</p>
        </div>
      );
    }
    if (type === PropertyType.Formula) {
      return <p className="text-sm text-default-500">{t("property.editor.formulaUnavailable")}</p>;
    }

    let value: unknown;

    switch (type) {
      case PropertyType.SingleLineText:
        value = t("property.editor.exampleText");
        break;
      case PropertyType.MultilineText:
        value = t("property.editor.exampleMultiline");
        break;
      case PropertyType.SingleChoice:
        value = options?.defaultValue || options?.choices?.[0]?.value;
        break;
      case PropertyType.MultipleChoice:
        value = options?.defaultValue?.length
          ? options.defaultValue
          : options?.choices?.slice(0, 3).map((c: { value: string }) => c.value);
        break;
      case PropertyType.Tags:
        value = options?.tags?.slice(0, 3).map((tag: { value: string }) => tag.value);
        break;
      case PropertyType.Multilevel: {
        const leaf = firstLeaf(options?.data);

        value = options?.defaultValue?.length ? options.defaultValue : leaf ? [leaf] : undefined;
        break;
      }
      case PropertyType.Boolean:
        value = false;
        break;
      case PropertyType.Link:
        value = { text: t("property.editor.exampleLink"), url: "https://example.com" };
        break;
      case PropertyType.Date:
      case PropertyType.DateTime:
        // Fixed example in the viewer's timezone, without an API lookup.
        return (
          <PropertyValueRenderer
            isReadonly
            dbValue={String(
              new Date(
                2026,
                0,
                15,
                type === PropertyType.Date ? 0 : 14,
                type === PropertyType.Date ? 0 : 30,
              ).getTime(),
            )}
            property={property}
          />
        );
      case PropertyType.Time:
        return <PropertyValueRenderer isReadonly dbValue="5400000" property={property} />;
    }
    if (value == null || (Array.isArray(value) && value.length === 0)) {
      return (
        <p className="text-sm leading-relaxed text-default-500">
          {t("property.editor.addOptionsToPreview")}
        </p>
      );
    }

    return (
      <PropertyValueRenderer
        isReadonly
        dbValue={serializeStandardValue(value, getDbValueType(type))}
        property={property}
      />
    );
  };

  return (
    <aside className="property-editor-preview min-w-0 self-start rounded-xl bg-default-50 p-4">
      <h3 className="text-xs font-medium text-default-500">{t("property.editor.preview")}</h3>
      <div className="mt-4 min-w-0">
        <div className="mb-2 break-words text-sm font-medium text-default-700">{property.name}</div>
        <div className="min-w-0 break-words">{renderExample()}</div>
      </div>
      <p className="mt-4 text-xs leading-relaxed text-default-500">
        {t("property.editor.previewHint")}
      </p>
    </aside>
  );
};

export default PropertyPreview;
