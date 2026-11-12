import { useTranslation } from "react-i18next";
import DependencyManager from "@/components/Dependency/DependencyManager";
import { dependencyManagerPath } from "@/components/Dependency/routes";
import { CloudDownloadSources, DownloadToolProxyTargets } from "@/sdk/constants";
import ProxyField from "../base/ProxyField";
import RcloneAccountsPanel from "@/components/CloudDownload/RcloneAccountsPanel";
import DownloadToolProxies from "@/components/ProxySelector/DownloadToolProxies";
import PikPakCliAccountsPanel from "@/components/CloudDownload/PikPakCliAccountsPanel";
import TeraBoxCliAccountsPanel from "@/components/CloudDownload/TeraBoxCliAccountsPanel";
import {
  DownloadMethodAvailabilityNotice,
  DownloadMethodStatusChip,
} from "@/components/CloudDownload/DownloadMethodAvailability";

/** The same provider preparation is available in third-party settings and at the point of download. */
export default function CloudSourceConfig({
  providerId,
  showDependencies = true,
  showAccounts = true,
}: {
  providerId: string;
  showDependencies?: boolean;
  showAccounts?: boolean;
}) {
  const { t } = useTranslation();
  const source = CloudDownloadSources.find((entry) => entry.providerId === providerId);
  if (!source) return null;
  return (
    <div className="space-y-4">
      <DownloadMethodAvailabilityNotice providerId={providerId} />
      <p className="text-sm leading-relaxed text-default-500">
        {t(`cloudDownload.source.${providerId}.description`)}
      </p>
      {showDependencies &&
        source.methods.map((method) => (
          <div key={method.id} className="space-y-2 rounded-lg border border-default-200 p-3">
            <p className="flex flex-wrap items-center gap-2 text-sm font-medium">
              {t(`acquire.methodName.${method.id}`, { defaultValue: method.displayName })}
              <DownloadMethodStatusChip methodId={method.id} />
            </p>
            {method.scope && (
              <p className="text-xs text-default-500">{t(`acquire.methodScope.${method.scope}`)}</p>
            )}
            {method.dependencyIds.map((id) => (
              <div key={id} className="space-y-2">
                <DependencyManager id={id} />
                <a href={`#${dependencyManagerPath(id)}`} className="text-sm text-primary">
                  {t("menu.dependencies")}
                </a>
              </div>
            ))}
            {method.engineId &&
              DownloadToolProxyTargets.some((target) => target.id === method.engineId) && (
                <DownloadToolProxies toolIds={[method.engineId]} />
              )}
          </div>
        ))}
      {showAccounts && source.methods.some((method) => method.accountProvider === "pikpakCli") && (
        <PikPakCliAccountsPanel />
      )}
      {showAccounts && source.methods.some((method) => method.accountProvider === "terabox") && (
        <TeraBoxCliAccountsPanel showMethodAvailability={false} />
      )}
      {showAccounts &&
        [
          ...new Set(
            source.methods
              .filter((method) => method.engineId === "rclone" && method.accountProvider)
              .map((method) => method.accountProvider!),
          ),
        ].map((accountProvider) => (
          <RcloneAccountsPanel key={accountProvider} providerId={accountProvider} />
        ))}
      <ProxyField thirdPartyId={source.thirdPartyId} />
    </div>
  );
}
