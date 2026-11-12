import { useTranslation } from "react-i18next";
import { Switch } from "@/components/bakaui";

export type ReusePolicy = "reuseVerified" | "force";

export function isReusePolicy(value: unknown): value is ReusePolicy {
  return value === "reuseVerified" || value === "force";
}

export default function ReusePolicyField({
  value = "reuseVerified",
  onChange,
  processing = false,
}: {
  value?: ReusePolicy;
  onChange: (value: ReusePolicy) => void;
  processing?: boolean;
}) {
  const { t } = useTranslation();
  return (
    <div className="space-y-2">
      <p className="text-xs text-default-500">{t("cloudDownload.reuse.description")}</p>
      <Switch
        isSelected={value === "force"}
        onValueChange={(force) => onChange(force ? "force" : "reuseVerified")}
      >
        {t(
          processing ? "cloudDownload.reuse.forceProcessing" : "cloudDownload.reuse.forceDownload",
        )}
      </Switch>
      {processing && (
        <p className="text-xs text-default-500">{t("cloudDownload.reuse.processingHint")}</p>
      )}
    </div>
  );
}
