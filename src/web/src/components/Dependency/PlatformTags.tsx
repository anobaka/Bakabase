import { useTranslation } from "react-i18next";
import { Chip } from "@/components/bakaui";
import type { DependencyContext } from "./presentation";

/** Support comes from the server's installation and execution declarations. */
export default function PlatformTags({ context }: { context: DependencyContext }) {
  const { t } = useTranslation();
  const targets = context.supportedTargets ?? [];
  return (
    <>
      {[...new Set(targets.map((target) => target.os))].map((os) => {
        const variants = targets.filter((target) => target.os === os);
        const label = t(`dependencies.os.${os}`, { defaultValue: os });
        const description = variants
          .map((target) =>
            [
              target.architecture,
              target.runtime,
              target.minimumVersion && `≥ ${target.minimumVersion}`,
              target.notes,
            ]
              .filter(Boolean)
              .join(" · "),
          )
          .join("; ");
        return (
          <Chip
            key={os}
            size="sm"
            variant="bordered"
            color={
              context.currentOs === os && context.isAvailableOnCurrentPlatform
                ? "primary"
                : "default"
            }
            title={`${label}: ${description}`}
          >
            {label}
          </Chip>
        );
      })}
    </>
  );
}
