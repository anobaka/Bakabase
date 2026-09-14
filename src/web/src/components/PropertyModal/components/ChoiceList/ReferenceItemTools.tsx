import { useTranslation } from "react-i18next";
import { DeleteOutlined, EyeInvisibleOutlined, EyeOutlined } from "@ant-design/icons";

import ReferenceValueUsage from "../ReferenceValueUsage";

import { Button, ColorPicker, Modal, toast } from "@/components/bakaui";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { buildColorValueString } from "@/components/bakaui/components/ColorPicker";
import colors from "@/components/bakaui/colors";

export function ReferenceColor({
  color,
  onChange,
}: {
  color?: string;
  onChange: (color: string) => void;
}) {
  const { t } = useTranslation();

  return (
    <ColorPicker
      color={color ?? colors.color}
      trigger={
        <button
          aria-label={t("property.referenceEditor.color")}
          className="flex h-8 w-8 shrink-0 items-center justify-center rounded-lg transition-colors hover:bg-default-200 focus-visible:outline focus-visible:outline-2 focus-visible:outline-focus"
          title={t("property.referenceEditor.color")}
          type="button"
        >
          <span
            className="h-4 w-4 rounded-md shadow-sm"
            style={{ backgroundColor: color ?? colors.color }}
          />
        </button>
      }
      onChange={(next) => onChange(buildColorValueString(next))}
    />
  );
}

export function ReferenceItemActions({
  value,
  label,
  hidden,
  onToggleHidden,
  onRemove,
  checkUsage,
}: {
  value: string;
  label?: string;
  hidden?: boolean;
  onToggleHidden: () => void;
  onRemove: () => void;
  checkUsage?: (value: string) => Promise<number>;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const visibilityLabel = t(
    hidden ? "property.referenceEditor.show" : "property.referenceEditor.hide",
  );

  return (
    <div className="flex shrink-0 items-center gap-0.5">
      <ReferenceValueUsage label={label} value={value} />
      <Button
        isIconOnly
        aria-label={visibilityLabel}
        aria-pressed={!!hidden}
        className={hidden ? "text-warning" : "text-default-400"}
        size="sm"
        title={visibilityLabel}
        variant="light"
        onPress={onToggleHidden}
      >
        {hidden ? <EyeInvisibleOutlined /> : <EyeOutlined />}
      </Button>
      <Button
        isIconOnly
        aria-label={t("property.referenceEditor.delete")}
        className="text-default-400 hover:text-danger"
        color="danger"
        size="sm"
        title={t("property.referenceEditor.delete")}
        variant="light"
        onPress={async () => {
          let count: number;

          try {
            count = checkUsage ? await checkUsage(value) : 0;
          } catch {
            toast.danger(t("property.referenceEditor.usageCheckFailed"));

            return;
          }

          if (count > 0) {
            createPortal(Modal, {
              defaultVisible: true,
              size: "sm",
              title: t("property.referenceEditor.deleteReferencedTitle", { count }),
              children: t("property.referenceEditor.deleteReferencedDescription"),
              onOk: onRemove,
            });
          } else {
            onRemove();
          }
        }}
      >
        <DeleteOutlined />
      </Button>
    </div>
  );
}
