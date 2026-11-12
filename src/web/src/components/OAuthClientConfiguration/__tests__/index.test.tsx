import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, expect, it, vi } from "vitest";
import OAuthClientConfiguration from "..";

const api = vi.hoisted(() => ({
  getOAuthProviderConfiguration: vi.fn(),
  saveOAuthProviderConfiguration: vi.fn(),
}));
vi.mock("@/sdk/BApi", () => ({ default: { oauth: api } }));
vi.mock("react-i18next", () => ({ useTranslation: () => ({ t: (key: string) => key }) }));
vi.mock("@/components/ExternalLink", () => ({
  default: ({ href, children }: any) => <a href={href}>{children}</a>,
}));
vi.mock("@/components/bakaui", () => ({
  Button: ({ children, onPress, isDisabled }: any) => (
    <button disabled={isDisabled} onClick={onPress}>
      {children}
    </button>
  ),
  Input: ({ label, description, type, value, onValueChange, isDisabled, autoComplete }: any) => (
    <div>
      <label>
        {label}
        <input
          type={type}
          value={value}
          disabled={isDisabled}
          autoComplete={autoComplete}
          onChange={(event) => onValueChange(event.target.value)}
        />
      </label>
      <p>{description}</p>
    </div>
  ),
  Select: ({ label, selectedKeys, dataSource, isDisabled, onSelectionChange }: any) => (
    <label>
      {label}
      <select
        value={selectedKeys[0]}
        disabled={isDisabled}
        onChange={(event) => onSelectionChange(new Set([event.target.value]))}
      >
        {dataSource.map((item: any) => (
          <option key={item.value} value={item.value}>
            {item.label}
          </option>
        ))}
      </select>
    </label>
  ),
  Checkbox: ({ children, isSelected, isDisabled, onValueChange }: any) => (
    <label>
      <input
        type="checkbox"
        checked={isSelected}
        disabled={isDisabled}
        onChange={(event) => onValueChange(event.target.checked)}
      />
      {children}
    </label>
  ),
  Spinner: () => <span>loading</span>,
}));
const options = { showErrorToast: false, sensitiveResponse: true };
const manual = {
  providerId: "microsoftGraph",
  mode: "manual",
  clientId: "",
  tenantId: "common",
  callbackUrl: "",
  hasClientSecret: false,
  isReady: false,
};
const configured = {
  ...manual,
  mode: "automatic",
  clientId: "client-1",
  callbackUrl: "https://server.example/oauth/callback/microsoftGraph",
  hasClientSecret: true,
  isReady: true,
};
const props = {
  providerId: "microsoftGraph",
  title: "Microsoft",
  tenantPresets: ["common", "organizations", "consumers"].map((value) => ({ value, label: value })),
  helpLinks: [
    {
      href: "https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app",
      label: "Microsoft documentation",
    },
  ],
};
beforeEach(() => {
  vi.resetAllMocks();
  api.getOAuthProviderConfiguration.mockResolvedValue({ code: 0, data: manual });
  api.saveOAuthProviderConfiguration.mockResolvedValue({ code: 0, data: configured });
});

it("defaults to manual, exposes explicit app settings only for automatic mode, and reports unsaved changes", async () => {
  const status = vi.fn();
  render(<OAuthClientConfiguration {...props} onStatusChange={status} />);
  const mode = await screen.findByLabelText("oauth.configuration.mode");
  await waitFor(() => expect(mode).toBeEnabled());
  expect(mode).toHaveValue("manual");
  expect(screen.getByText("oauth.configuration.manualReady")).toBeVisible();
  expect(screen.queryByText("oauth.configuration.notReady")).toBeNull();
  expect(screen.queryByLabelText("oauth.configuration.clientSecret")).toBeNull();
  fireEvent.change(mode, { target: { value: "automatic" } });
  expect(screen.getByLabelText("oauth.configuration.tenant")).toHaveValue("common");
  expect(screen.getByLabelText("oauth.configuration.clientSecret")).toHaveAttribute(
    "type",
    "password",
  );
  expect(screen.getByRole("link", { name: "Microsoft documentation" })).toHaveAttribute(
    "href",
    props.helpLinks[0].href,
  );
  expect(screen.getByText("oauth.configuration.save")).toBeDisabled();
  await waitFor(() =>
    expect(status).toHaveBeenLastCalledWith(expect.objectContaining({ hasUnsavedChanges: true })),
  );
  expect(api.saveOAuthProviderConfiguration).not.toHaveBeenCalled();
});

it("saves client and tenant fields while omitting a blank secret to preserve the stored value", async () => {
  api.getOAuthProviderConfiguration.mockResolvedValue({ code: 0, data: configured });
  render(<OAuthClientConfiguration {...props} />);
  const secret = await screen.findByLabelText("oauth.configuration.clientSecret");
  expect(secret).toHaveValue("");
  expect(screen.getByText("oauth.configuration.secret.saved")).toBeVisible();
  fireEvent.change(screen.getByLabelText("oauth.configuration.tenant"), {
    target: { value: "organizations" },
  });
  fireEvent.click(screen.getByText("oauth.configuration.save"));
  await waitFor(() =>
    expect(api.saveOAuthProviderConfiguration).toHaveBeenCalledWith(
      "microsoftGraph",
      {
        mode: "automatic",
        clientId: "client-1",
        tenantId: "organizations",
        callbackUrl: configured.callbackUrl,
        clientSecret: undefined,
        clearClientSecret: false,
      },
      options,
    ),
  );
});

it("clears a newly submitted secret immediately, including failed saves", async () => {
  let finish!: (value: unknown) => void;
  api.getOAuthProviderConfiguration.mockResolvedValue({ code: 0, data: configured });
  api.saveOAuthProviderConfiguration.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  render(<OAuthClientConfiguration {...props} />);
  const secret = await screen.findByLabelText("oauth.configuration.clientSecret");
  fireEvent.change(secret, { target: { value: "fixture-private-secret" } });
  fireEvent.click(screen.getByText("oauth.configuration.save"));
  expect(secret).toHaveValue("");
  expect(api.saveOAuthProviderConfiguration).toHaveBeenCalledWith(
    "microsoftGraph",
    expect.objectContaining({ clientSecret: "fixture-private-secret" }),
    options,
  );
  await act(async () => {
    finish({ code: 400, message: "OAUTH_CONFIGURATION_SAVE_FAILED" });
  });
  expect(secret).toHaveValue("");
  expect(await screen.findByRole("alert")).toHaveTextContent(
    "oauth.problem.OAUTH_CONFIGURATION_SAVE_FAILED",
  );
});

it("allows explicit secret removal in manual mode and sends empty strings to clear app fields", async () => {
  api.getOAuthProviderConfiguration.mockResolvedValue({ code: 0, data: configured });
  api.saveOAuthProviderConfiguration.mockResolvedValue({ code: 0, data: manual });
  render(<OAuthClientConfiguration {...props} />);
  fireEvent.change(await screen.findByLabelText("oauth.configuration.clientId"), {
    target: { value: "" },
  });
  fireEvent.change(screen.getByLabelText("oauth.configuration.callbackUrl"), {
    target: { value: "" },
  });
  fireEvent.change(screen.getByLabelText("oauth.configuration.mode"), {
    target: { value: "manual" },
  });
  fireEvent.click(screen.getByLabelText("oauth.configuration.secret.clear"));
  fireEvent.click(screen.getByText("oauth.configuration.save"));
  await waitFor(() =>
    expect(api.saveOAuthProviderConfiguration).toHaveBeenCalledWith(
      "microsoftGraph",
      {
        mode: "manual",
        clientId: "",
        tenantId: "common",
        callbackUrl: "",
        clientSecret: undefined,
        clearClientSecret: true,
      },
      options,
    ),
  );
});

it("explains that changing the app requires a new secret and honors server readiness after save", async () => {
  api.getOAuthProviderConfiguration.mockResolvedValue({ code: 0, data: configured });
  api.saveOAuthProviderConfiguration.mockResolvedValue({
    code: 0,
    data: { ...configured, clientId: "client-2", hasClientSecret: false, isReady: false },
  });
  const status = vi.fn();
  render(<OAuthClientConfiguration {...props} onStatusChange={status} />);
  fireEvent.change(await screen.findByLabelText("oauth.configuration.clientId"), {
    target: { value: "client-2" },
  });
  expect(screen.getByText("oauth.configuration.secret.appChanged")).toBeVisible();
  fireEvent.click(screen.getByText("oauth.configuration.save"));
  await screen.findByText("oauth.configuration.notReady");
  expect(screen.getByLabelText("oauth.configuration.clientSecret")).toHaveValue("");
  await waitFor(() =>
    expect(status).toHaveBeenLastCalledWith({
      mode: "automatic",
      isReady: false,
      hasUnsavedChanges: false,
    }),
  );
});

it("discards a previous provider's delayed save without restoring its configuration or secret", async () => {
  let finish!: (value: unknown) => void;
  api.getOAuthProviderConfiguration
    .mockResolvedValueOnce({ code: 0, data: configured })
    .mockResolvedValueOnce({ code: 0, data: { ...manual, providerId: "anotherProvider" } });
  api.saveOAuthProviderConfiguration.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  const { rerender } = render(<OAuthClientConfiguration {...props} />);
  fireEvent.change(await screen.findByLabelText("oauth.configuration.clientSecret"), {
    target: { value: "fixture-private-secret" },
  });
  fireEvent.click(screen.getByText("oauth.configuration.save"));
  rerender(<OAuthClientConfiguration {...props} providerId="anotherProvider" />);
  await waitFor(() =>
    expect(screen.getByLabelText("oauth.configuration.mode")).toHaveValue("manual"),
  );
  await act(async () => {
    finish({ code: 0, data: configured });
  });
  expect(screen.getByLabelText("oauth.configuration.mode")).toHaveValue("manual");
  expect(screen.queryByDisplayValue("fixture-private-secret")).toBeNull();
  expect(screen.queryByLabelText("oauth.configuration.clientId")).toBeNull();
});
