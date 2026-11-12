import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Checkbox, Input, Select, Spinner } from "@/components/bakaui";
import ExternalLink from "@/components/ExternalLink";
import BApi from "@/sdk/BApi";

type Configuration = NonNullable<
  Awaited<ReturnType<typeof BApi.oauth.getOAuthProviderConfiguration>>["data"]
>;
const requestOptions = { showErrorToast: false, sensitiveResponse: true };
export type OAuthConfigurationStatus = {
  mode: string;
  isReady: boolean;
  hasUnsavedChanges: boolean;
};

/** Provider-neutral client settings. Credentials are only written, never read back or cached. */
export default function OAuthClientConfiguration({
  providerId,
  title,
  tenantPresets,
  helpLinks = [],
  isDisabled = false,
  onStatusChange,
}: {
  providerId: string;
  title: string;
  tenantPresets?: { value: string; label: string }[];
  helpLinks?: { href: string; label: string }[];
  isDisabled?: boolean;
  onStatusChange?: (status: OAuthConfigurationStatus | undefined) => void;
}) {
  const { t } = useTranslation();
  const [configuration, setConfiguration] = useState<Configuration>();
  const [mode, setMode] = useState("manual");
  const [clientId, setClientId] = useState("");
  const [tenantId, setTenantId] = useState(tenantPresets?.[0]?.value ?? "");
  const [callbackUrl, setCallbackUrl] = useState("");
  const [clientSecret, setClientSecret] = useState("");
  const [clearClientSecret, setClearClientSecret] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const generation = useRef(0);
  const pending = useRef(false);
  const dirty =
    !!configuration &&
    (mode !== configuration.mode ||
      clientId.trim() !== (configuration.clientId ?? "") ||
      tenantId.trim() !== (configuration.tenantId ?? tenantPresets?.[0]?.value ?? "") ||
      callbackUrl.trim() !== (configuration.callbackUrl ?? "") ||
      !!clientSecret ||
      clearClientSecret);
  useEffect(() => {
    onStatusChange?.(
      configuration
        ? {
            mode: configuration.mode,
            isReady: configuration.isReady,
            hasUnsavedChanges: dirty || busy,
          }
        : undefined,
    );
  }, [configuration, dirty, busy, onStatusChange]);
  const problemLabel = (message: string) => {
    const code = message.match(/\bOAUTH_[A-Z][A-Z0-9_]*\b/)?.[0];
    return code ? t(`oauth.problem.${code}`, { defaultValue: message }) : message;
  };
  const apply = (value: Configuration) => {
    setConfiguration(value);
    setMode(value.mode);
    setClientId(value.clientId ?? "");
    setTenantId(value.tenantId ?? tenantPresets?.[0]?.value ?? "");
    setCallbackUrl(value.callbackUrl ?? "");
    setClientSecret("");
    setClearClientSecret(false);
  };
  const load = async () => {
    if (pending.current) return;
    const current = generation.current;
    pending.current = true;
    setBusy(true);
    setError(undefined);
    try {
      const response = await BApi.oauth.getOAuthProviderConfiguration(providerId, requestOptions);
      if (response.code || !response.data)
        throw new Error(response.message || "OAUTH_CONFIGURATION_LOAD_FAILED");
      if (current === generation.current) apply(response.data);
    } catch (e) {
      if (current === generation.current) setError(e instanceof Error ? e.message : String(e));
    } finally {
      if (current === generation.current) {
        pending.current = false;
        setBusy(false);
      }
    }
  };
  useEffect(() => {
    generation.current++;
    pending.current = false;
    setConfiguration(undefined);
    setMode("manual");
    setClientId("");
    setTenantId(tenantPresets?.[0]?.value ?? "");
    setCallbackUrl("");
    setClientSecret("");
    setClearClientSecret(false);
    void load();
    return () => {
      generation.current++;
    };
  }, [providerId]);
  const save = async () => {
    if (pending.current || isDisabled || !configuration) return;
    const current = generation.current;
    const secret = clientSecret;
    setClientSecret("");
    pending.current = true;
    setBusy(true);
    setError(undefined);
    try {
      const response = await BApi.oauth.saveOAuthProviderConfiguration(
        providerId,
        {
          mode,
          clientId: clientId.trim(),
          tenantId: tenantId.trim() || tenantPresets?.[0]?.value || undefined,
          callbackUrl: callbackUrl.trim(),
          clientSecret: !clearClientSecret && secret ? secret : undefined,
          clearClientSecret,
        },
        requestOptions,
      );
      if (response.code || !response.data)
        throw new Error(response.message || "OAUTH_CONFIGURATION_SAVE_FAILED");
      if (current === generation.current) apply(response.data);
    } catch (e) {
      if (current === generation.current) setError(e instanceof Error ? e.message : String(e));
    } finally {
      if (current === generation.current) {
        pending.current = false;
        setBusy(false);
      }
    }
  };
  const disabled = isDisabled || busy || !configuration;
  const preset = tenantPresets?.some((value) => value.value === tenantId) ? tenantId : "custom";

  return (
    <div className="space-y-3 rounded-lg border border-default-200 p-3">
      <p className="text-sm font-medium">{title}</p>
      {!configuration && busy && <Spinner size="sm" />}
      <Select
        label={t("oauth.configuration.mode")}
        selectedKeys={[mode]}
        isDisabled={disabled}
        disallowEmptySelection
        dataSource={["manual", "automatic"].map((value) => ({
          value,
          label: t(`oauth.configuration.mode.${value}`),
        }))}
        onSelectionChange={(keys) => {
          const value = String(Array.from(keys)[0] ?? "");
          if (value === "manual" || value === "automatic") {
            setClientSecret("");
            setClearClientSecret(false);
            setMode(value);
          }
        }}
      />
      <p className="text-xs text-default-500">{t(`oauth.configuration.hint.${mode}`)}</p>
      {mode === "automatic" && (
        <>
          <Input
            label={t("oauth.configuration.clientId")}
            value={clientId}
            onValueChange={setClientId}
            isRequired
            isDisabled={disabled}
            autoComplete="off"
          />
          {tenantPresets && (
            <>
              <Select
                label={t("oauth.configuration.tenant")}
                selectedKeys={[preset]}
                isDisabled={disabled}
                disallowEmptySelection
                dataSource={[
                  ...tenantPresets,
                  { value: "custom", label: t("oauth.configuration.tenant.custom") },
                ]}
                onSelectionChange={(keys) => {
                  const value = String(Array.from(keys)[0] ?? "");
                  setTenantId(value === "custom" ? "" : value);
                }}
              />
              {preset === "custom" && (
                <Input
                  label={t("oauth.configuration.tenantId")}
                  value={tenantId}
                  onValueChange={setTenantId}
                  isRequired
                  isDisabled={disabled}
                  autoComplete="off"
                />
              )}
            </>
          )}
          <Input
            label={t("oauth.configuration.callbackUrl")}
            description={t("oauth.configuration.callbackHint", {
              path: `/oauth/callback/${providerId}`,
            })}
            placeholder={`https://your-server.example/oauth/callback/${providerId}`}
            value={callbackUrl}
            onValueChange={setCallbackUrl}
            isRequired
            isDisabled={disabled}
            autoComplete="off"
          />
          <Input
            label={t("oauth.configuration.clientSecret")}
            description={t(
              configuration?.hasClientSecret && clientId.trim() !== (configuration.clientId ?? "")
                ? "oauth.configuration.secret.appChanged"
                : configuration?.hasClientSecret
                  ? "oauth.configuration.secret.saved"
                  : "oauth.configuration.secret.required",
            )}
            type="password"
            autoComplete="new-password"
            value={clientSecret}
            onValueChange={setClientSecret}
            isDisabled={disabled || clearClientSecret}
          />
          <div className="flex flex-wrap gap-3">
            {helpLinks.map((link) => (
              <ExternalLink key={link.href} href={link.href} size="sm">
                {link.label}
              </ExternalLink>
            ))}
          </div>
        </>
      )}
      {configuration?.hasClientSecret && (
        <Checkbox
          isSelected={clearClientSecret}
          isDisabled={disabled}
          onValueChange={(value) => {
            setClientSecret("");
            setClearClientSecret(value);
          }}
        >
          {t("oauth.configuration.secret.clear")}
        </Checkbox>
      )}
      <p className="text-xs text-default-500">{t("oauth.configuration.newSessionHint")}</p>
      {dirty && <p className="text-xs text-warning-600">{t("oauth.configuration.unsaved")}</p>}
      {configuration && (
        <p className="text-sm text-default-600">
          {t(
            configuration.mode === "manual"
              ? "oauth.configuration.manualReady"
              : configuration.isReady
                ? "oauth.configuration.ready"
                : "oauth.configuration.notReady",
          )}
        </p>
      )}
      {configuration?.problem && (
        <p className="text-sm text-warning-600">{problemLabel(configuration.problem)}</p>
      )}
      {error && (
        <p role="alert" className="text-sm text-danger">
          {problemLabel(error)}
        </p>
      )}
      <div className="flex gap-2">
        <Button
          size="sm"
          color="primary"
          isLoading={busy}
          isDisabled={
            disabled ||
            (mode === "automatic" &&
              (!clientId.trim() || !callbackUrl.trim() || (tenantPresets && !tenantId.trim())))
          }
          onPress={() => void save()}
        >
          {t("oauth.configuration.save")}
        </Button>
        <Button
          size="sm"
          variant="light"
          isDisabled={busy || isDisabled}
          onPress={() => void load()}
        >
          {t("oauth.configuration.refresh")}
        </Button>
      </div>
    </div>
  );
}
