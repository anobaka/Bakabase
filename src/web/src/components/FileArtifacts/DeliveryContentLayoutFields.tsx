import { useTranslation } from "react-i18next";
import { Button, Checkbox } from "@/components/bakaui";
import type { BakabaseModulesWorkflowArtifactsArtifactDeliveryPolicy as DeliveryContentLayout } from "@/sdk/Api";

export type { DeliveryContentLayout };

export const defaultDeliveryContentLayout = (): DeliveryContentLayout => ({
  version: 1,
  removeGeneratedDirectories: true,
  collapseSingleRootDirectory: false,
});

export function deliveryContentLayoutIsValid(value: unknown): boolean {
  return (
    value == null ||
    (typeof value === "object" &&
      ((value as DeliveryContentLayout).version === undefined ||
        (value as DeliveryContentLayout).version === 1) &&
      typeof (value as DeliveryContentLayout).removeGeneratedDirectories === "boolean" &&
      typeof (value as DeliveryContentLayout).collapseSingleRootDirectory === "boolean")
  );
}

export function deliveryContentLayoutsEqual(
  left?: DeliveryContentLayout | null,
  right?: DeliveryContentLayout | null,
): boolean {
  return left == null || right == null
    ? left == null && right == null
    : (left.version ?? 1) === (right.version ?? 1) &&
        left.removeGeneratedDirectories === right.removeGeneratedDirectories &&
        left.collapseSingleRootDirectory === right.collapseSingleRootDirectory;
}

export default function DeliveryContentLayoutFields({
  value,
  onChange,
  disabled,
  showExample = true,
}: {
  value?: DeliveryContentLayout | null;
  onChange: (value: DeliveryContentLayout) => void;
  disabled?: boolean;
  showExample?: boolean;
}) {
  const { t } = useTranslation();
  // Missing policies belong to existing plans. Merely opening this form must not migrate them.
  const exampleBefore = "bakabase-example/Author folder/images/01.jpg";
  const exampleAfter = !value?.removeGeneratedDirectories
    ? exampleBefore
    : value.collapseSingleRootDirectory
      ? "images/01.jpg"
      : "Author folder/images/01.jpg";
  return (
    <section className="space-y-2" aria-label={t("artifact.deliveryLayout.title")}>
      <h3 className="text-sm font-medium">{t("artifact.deliveryLayout.title")}</h3>
      {value == null ? (
        <div className="flex flex-wrap items-center gap-2">
          <p className="text-xs text-default-500">{t("artifact.deliveryLayout.legacy")}</p>
          <Button
            size="sm"
            variant="flat"
            isDisabled={disabled}
            onPress={() => onChange(defaultDeliveryContentLayout())}
          >
            {t("artifact.deliveryLayout.configure")}
          </Button>
        </div>
      ) : (
        <>
          <div>
            <Checkbox
              isDisabled={disabled}
              isSelected={value.removeGeneratedDirectories}
              onValueChange={(removeGeneratedDirectories) =>
                onChange({ ...value, removeGeneratedDirectories })
              }
            >
              {t("artifact.deliveryLayout.removeGenerated")}
            </Checkbox>
            <p className="pl-7 text-xs text-default-500">
              {t("artifact.deliveryLayout.removeGeneratedHint")}
            </p>
          </div>
          <div>
            <Checkbox
              isDisabled={disabled}
              isSelected={value.collapseSingleRootDirectory}
              onValueChange={(collapseSingleRootDirectory) =>
                onChange({ ...value, collapseSingleRootDirectory })
              }
            >
              {t("artifact.deliveryLayout.collapseSingleRoot")}
            </Checkbox>
            <p className="pl-7 text-xs text-default-500">
              {t("artifact.deliveryLayout.collapseHint")}
            </p>
          </div>
          {showExample && (
            <div className="space-y-1 rounded-lg bg-default-100 p-2 text-xs" aria-live="polite">
              <p className="text-default-500">{t("artifact.deliveryLayout.exampleHint")}</p>
              <p>
                {t("artifact.deliveryLayout.before")}: <code>{exampleBefore}</code>
              </p>
              <p>
                {t("artifact.deliveryLayout.after")}: <code>{exampleAfter}</code>
              </p>
            </div>
          )}
        </>
      )}
    </section>
  );
}
