import { lazy, Suspense, useState, type ComponentType } from "react";
import { useTranslation } from "react-i18next";
import { Button, Spinner } from "@/components/bakaui";
import DependencyManager from "@/components/Dependency/DependencyManager";
import { dependencyManagerPath } from "@/components/Dependency/routes";
import AccountSelect from "@/components/CloudDownload/AccountSelect";
import CloudSourceConfig from "@/components/ThirdPartyConfig/platforms/CloudSourceConfig";
import { CloudDownloadSources, DownloadToolProxyTargets, ThirdPartyId } from "@/sdk/constants";
import { getEnumKey } from "@/i18n";
import DownloadToolProxies from "@/components/ProxySelector/DownloadToolProxies";
import RcloneAccountSelect from "@/components/CloudDownload/RcloneAccountSelect";
import RcloneAccountsPanel from "@/components/CloudDownload/RcloneAccountsPanel";
import type { DownloadMethod, DownloadRequirement } from "./models";

const BaiduAccountSettings = lazy(
  () => import("@/components/ThirdPartyConfig/platforms/BaiduNetdiskConfig"),
);

/** Backend capability IDs select method-specific interaction; the acquisition form stays source-neutral. */
const accountInteractions: Record<
  string,
  {
    Select?: ComponentType<{
      value?: string;
      onChange: (id: string | undefined) => void;
      isDisabled?: boolean;
    }>;
    Settings: ComponentType<{ onChanged?: () => void; showMethodAvailability?: boolean }>;
  }
> = {
  baidu: { Select: AccountSelect, Settings: BaiduAccountSettings },
  pikpakCli: { Settings: lazy(() => import("@/components/CloudDownload/PikPakCliAccountsPanel")) },
  terabox: { Settings: lazy(() => import("@/components/CloudDownload/TeraBoxCliAccountsPanel")) },
  exHentai: { Settings: lazy(() => import("./ExHentaiPreparation")) },
};

export default function MethodPreparation({
  methods,
  requirements,
  accountId,
  onAccountChange,
  onRefresh,
  refreshKey,
  isDisabled = false,
  accountSelectionDisabled = false,
}: {
  methods: DownloadMethod[];
  requirements: DownloadRequirement[];
  accountId?: string;
  onAccountChange: (id: string | undefined) => void;
  onRefresh: () => void;
  refreshKey?: number;
  isDisabled?: boolean;
  accountSelectionDisabled?: boolean;
}) {
  const { t } = useTranslation();
  const [expanded, setExpanded] = useState(false);
  const dependencies = [...new Set(methods.flatMap((method) => method.dependencyIds))];
  const toolIds = [
    ...new Set(
      methods.flatMap((method) =>
        method.engineId && DownloadToolProxyTargets.some((target) => target.id === method.engineId)
          ? [method.engineId]
          : [],
      ),
    ),
  ];
  const providers = [
    ...new Set(
      [
        ...methods.map((method) => method.accountProvider),
        ...requirements.map((requirement) => requirement.accountProvider),
      ].filter((value): value is string => !!value),
    ),
  ];
  const sourceSettings = CloudDownloadSources.filter((source) =>
    methods.some((method) => method.providerId === source.providerId),
  );
  const needsResolverNetwork = requirements.some(
    (requirement) => requirement.code === "SOURCE_NETWORK_REQUIRES_PROXY",
  );
  return (
    <div className="space-y-2">
      {providers.map((provider) => {
        const rclone = methods.some(
          (method) => method.engineId === "rclone" && method.accountProvider === provider,
        );
        if (rclone)
          return (
            <RcloneAccountSelect
              key={`${provider}:${refreshKey}`}
              providerId={provider}
              value={accountId}
              onChange={onAccountChange}
              isDisabled={isDisabled || accountSelectionDisabled}
            />
          );
        const interaction = accountInteractions[provider as keyof typeof accountInteractions];
        return interaction?.Select ? (
          <interaction.Select
            key={`${provider}:${refreshKey}`}
            value={accountId}
            onChange={onAccountChange}
            isDisabled={isDisabled || accountSelectionDisabled}
          />
        ) : null;
      })}
      {(dependencies.length > 0 ||
        providers.length > 0 ||
        sourceSettings.length > 0 ||
        needsResolverNetwork) && (
        <Button
          size="sm"
          variant="light"
          isDisabled={isDisabled}
          onPress={() => setExpanded((value) => !value)}
        >
          {t(expanded ? "acquire.preparation.hide" : "acquire.preparation.open")}
        </Button>
      )}
      {expanded && (
        <div className="space-y-3 rounded-lg border border-default-200 p-3">
          {needsResolverNetwork && <DownloadToolProxies toolIds={["sourceResolver"]} />}
          {toolIds.length > 0 && <DownloadToolProxies toolIds={toolIds} />}
          {dependencies.map((id) => (
            <div key={id} className="space-y-2">
              <DependencyManager id={id} />
              <a href={`#${dependencyManagerPath(id)}`} className="text-sm text-primary">
                {t("menu.dependencies")}
              </a>
            </div>
          ))}
          {providers.map((provider) => {
            if (
              methods.some(
                (method) => method.engineId === "rclone" && method.accountProvider === provider,
              )
            )
              return (
                <RcloneAccountsPanel key={provider} providerId={provider} onChanged={onRefresh} />
              );
            const interaction = accountInteractions[provider as keyof typeof accountInteractions];
            return interaction ? (
              <Suspense key={provider} fallback={<Spinner size="sm" />}>
                <interaction.Settings onChanged={onRefresh} showMethodAvailability={false} />
              </Suspense>
            ) : null;
          })}
          {sourceSettings.map((source) => (
            <div key={source.providerId} className="space-y-2">
              <p className="text-sm font-medium">
                {t(getEnumKey("ThirdPartyId", ThirdPartyId[source.thirdPartyId]))}
              </p>
              <CloudSourceConfig
                providerId={source.providerId}
                showDependencies={false}
                showAccounts={false}
              />
            </div>
          ))}
          <Button size="sm" variant="flat" isDisabled={isDisabled} onPress={onRefresh}>
            {t("acquire.preparation.refresh")}
          </Button>
        </div>
      )}
    </div>
  );
}
