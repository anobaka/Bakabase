import { useTranslation } from "react-i18next";

import { Button } from "@/components/bakaui";

type Props = {
  errors: Map<string, string>;
  checking: boolean;
  onRetry: () => void;
  onManage?: () => void;
};

export default function PathCheckWarnings({ errors, checking, onRetry, onManage }: Props) {
  const { t } = useTranslation();

  if (errors.size === 0) return null;

  return (
    <div
      className="space-y-2 rounded-lg border border-warning-200 bg-warning-50 p-3 text-sm"
      role="alert"
    >
      <p className="font-medium">{t("pathMarkConfig.pathCheck.title", { count: errors.size })}</p>
      <p>{t("pathMarkConfig.pathCheck.description")}</p>
      <ul className="max-h-32 overflow-auto space-y-1">
        {[...errors].map(([path, message]) => (
          <li key={path} className="break-all">
            <code>{path}</code>: {message}
          </li>
        ))}
      </ul>
      <div className="flex gap-2">
        <Button isLoading={checking} size="sm" onPress={onRetry}>
          {t("pathMarkConfig.pathCheck.retry")}
        </Button>
        {onManage && (
          <Button size="sm" variant="flat" onPress={onManage}>
            {t("pathMarkConfig.pathCheck.managePaths")}
          </Button>
        )}
      </div>
    </div>
  );
}
