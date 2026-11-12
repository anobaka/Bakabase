import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import MethodPreparation from "../MethodPreparation";
import CloudSourceConfig from "@/components/ThirdPartyConfig/platforms/CloudSourceConfig";
import type { DownloadMethod } from "../models";
import cnCloud from "@/locales/cn/pages/cloudDownload.json";
import enCloud from "@/locales/en/pages/cloudDownload.json";
import cnAcquire from "@/locales/cn/components/acquire.json";
import enAcquire from "@/locales/en/components/acquire.json";
import cnConfig from "@/locales/cn/pages/configuration.json";
import enConfig from "@/locales/en/pages/configuration.json";
import { dependencyPresentation } from "@/components/Dependency/presentation";

vi.mock("@/sdk/constants", async (original) => ({
  ...(await original<object>()),
  CloudDownloadSources: [
    {
      providerId: "terabox",
      thirdPartyId: 21,
      methods: [
        {
          id: "terabox.cli",
          displayName: "TeraBox CLI",
          engineId: "terabox-cli",
          accountProvider: "terabox",
          dependencyIds: ["terabox-cli-component-service"],
          scope: "publicShare",
        },
      ],
    },
  ],
  DownloadToolProxyTargets: [{ id: "terabox-cli" }],
  ThirdPartyId: { 21: "TeraBox", TeraBox: 21 },
}));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/i18n", () => ({ getEnumKey: (name: string, value: string) => `${name}.${value}` }));
vi.mock("@/components/Dependency/DependencyManager", () => ({
  default: ({ id }: any) => <div data-testid="dependency">{id}</div>,
}));
vi.mock("@/components/CloudDownload/TeraBoxCliAccountsPanel", () => ({
  default: ({ onChanged }: any) => <button onClick={onChanged}>terabox-account-panel</button>,
}));
vi.mock("@/components/CloudDownload/PikPakCliAccountsPanel", () => ({
  default: () => <div>pikpak-panel</div>,
}));
vi.mock("@/components/CloudDownload/RcloneAccountsPanel", () => ({
  default: () => <div>rclone-panel</div>,
}));
vi.mock("@/components/CloudDownload/RcloneAccountSelect", () => ({
  default: () => <div>rclone-select</div>,
}));
vi.mock("@/components/CloudDownload/AccountSelect", () => ({
  default: () => <div>baidu-select</div>,
}));
vi.mock("@/components/ThirdPartyConfig/base/ProxyField", () => ({
  default: ({ thirdPartyId }: any) => <div data-testid="provider-proxy">{thirdPartyId}</div>,
}));
vi.mock("@/components/ProxySelector/DownloadToolProxies", () => ({
  default: ({ toolIds }: any) => <div data-testid="tool-proxy">{toolIds.join(",")}</div>,
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Spinner: () => <span>loading</span>,
  Chip: ({ children }: any) => <span>{children}</span>,
  Tooltip: ({ children }: any) => <>{children}</>,
}));
afterEach(cleanup);

it("reuses the TeraBox account, dependency and two proxy scopes in third-party configuration", () => {
  render(<CloudSourceConfig providerId="terabox" />);
  expect(screen.getByText("terabox-account-panel")).toBeTruthy();
  expect(screen.getByTestId("dependency")).toHaveTextContent("terabox-cli-component-service");
  expect(screen.getByTestId("tool-proxy")).toHaveTextContent("terabox-cli");
  expect(screen.getByTestId("provider-proxy")).toHaveTextContent("21");
  expect(screen.queryByText("pikpak-panel")).toBeNull();
  expect(screen.queryByText("rclone-panel")).toBeNull();
});

it("honors account/dependency suppression for shared in-place source descriptions", () => {
  render(<CloudSourceConfig providerId="terabox" showAccounts={false} showDependencies={false} />);
  expect(screen.getByText("cloudDownload.source.terabox.description")).toBeTruthy();
  expect(screen.queryByText("terabox-account-panel")).toBeNull();
  expect(screen.queryByTestId("dependency")).toBeNull();
});

it("routes missing TeraBox account requirements to in-place authorization and refreshes the caller", async () => {
  const onRefresh = vi.fn();
  const method: DownloadMethod = {
    id: "terabox.cli",
    displayName: "TeraBox CLI",
    providerId: "terabox",
    transport: "site",
    supported: true,
    ready: false,
    state: "needsSetup",
    dependencyIds: ["terabox-cli-component-service"],
    requirements: [],
    supportsTransfer: false,
    accountProvider: "terabox",
    engineId: "terabox-cli",
  };
  render(
    <MethodPreparation
      methods={[method]}
      requirements={[
        {
          code: "CLOUD_ACCOUNT_REQUIRED",
          kind: "account",
          message: "Connect",
          accountProvider: "terabox",
          actions: ["manageAccount"],
        },
      ]}
      onAccountChange={() => undefined}
      onRefresh={onRefresh}
    />,
  );
  fireEvent.click(screen.getByText("acquire.preparation.open"));
  fireEvent.click(await screen.findByText("terabox-account-panel"));
  expect(onRefresh).toHaveBeenCalledTimes(1);
  expect(screen.getAllByText("terabox-account-panel")).toHaveLength(1);
  expect(screen.getByTestId("dependency")).toHaveTextContent("terabox-cli-component-service");
  expect(screen.queryByText("rclone-select")).toBeNull();
  expect(screen.queryByText("baidu-select")).toBeNull();
});

it("presents TeraBox as an optional download dependency and translates the method and account requirements", () => {
  const presentation = dependencyPresentation(
    { id: "terabox-cli-component-service" } as any,
    ((key: string) => key) as any,
  );
  expect(presentation.group).toBe("download");
  expect(presentation.purpose).toBe("dependencies.purpose.teraboxCli");
  for (const [cloud, acquire, configuration] of [
    [cnCloud, cnAcquire, cnConfig],
    [enCloud, enAcquire, enConfig],
  ] as const) {
    expect(cloud["cloudDownload.source.terabox.description"]).toBeTruthy();
    expect(acquire["acquire.methodName.terabox.cli"]).toBeTruthy();
    expect(acquire["acquire.requirement.TERABOX_ACCOUNT_REQUIRED"]).toBeTruthy();
    expect(acquire["acquire.requirement.CLOUD_ACCOUNT_REQUIRED"]).toBeTruthy();
    expect(configuration["dependencies.purpose.teraboxCli"]).toBeTruthy();
    expect(configuration["configuration.downloadTools.name.terabox-cli"]).toBeTruthy();
  }
  const keys = Object.keys(cnCloud).filter((key) => key.startsWith("terabox."));
  expect(keys.length).toBeGreaterThan(20);
  for (const key of keys) expect(enCloud[key as keyof typeof enCloud], key).toBeTruthy();
});
