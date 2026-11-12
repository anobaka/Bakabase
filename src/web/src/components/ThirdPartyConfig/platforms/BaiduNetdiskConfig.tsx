import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";

import { Button, Input, Spinner, Switch } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import DependencyComponent from "@/components/Dependency/DependencyManager";
import ExternalLink from "@/components/ExternalLink";
import DownloadToolProxies from "@/components/ProxySelector/DownloadToolProxies";
import { ThirdPartyId } from "@/sdk/constants";
import type { BakabaseServiceComponentsCloudDownloadsBaiduBaiduPcsAccountViewModel } from "@/sdk/Api";
import ProxyField from "../base/ProxyField";
import BaiduDownloadSettings from "./BaiduDownloadSettings";

export type BaiduAccount = BakabaseServiceComponentsCloudDownloadsBaiduBaiduPcsAccountViewModel;

export const BAIDU_COMPONENT_ID = "baidu-pcs-go-component-service";

/** Credentials are write-only: an existing account is reauthenticated with a fresh input. */
export default function BaiduNetdiskConfig({ onChanged }: { onChanged?: () => void } = {}) {
  const { t } = useTranslation();
  const [accounts, setAccounts] = useState<BaiduAccount[]>();
  const [editingId, setEditingId] = useState<string>();
  const [name, setName] = useState("");
  const [bduss, setBduss] = useState("");
  const [stoken, setStoken] = useState("");
  const [cookies, setCookies] = useState("");
  const [cookieMode, setCookieMode] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string>();
  const load = async () => {
    const response = await BApi.baiduPcs.getBaiduPcsAccounts();
    if (response.code) throw new Error(response.message ?? t("cloudDownload.error"));
    setAccounts(response.data ?? []);
  };
  useEffect(() => {
    void load().catch((e: unknown) => setError(e instanceof Error ? e.message : String(e)));
  }, []);
  const perform = async (operation: () => Promise<{ code?: number; message?: string | null }>) => {
    if (busy) return;
    setBusy(true);
    setError(undefined);
    try {
      const response = await operation();
      if (response.code) throw new Error(response.message ?? t("cloudDownload.error"));
      await load();
      onChanged?.();
    } catch (e) {
      setError(e instanceof Error ? e.message : t("cloudDownload.error"));
    } finally {
      setBusy(false);
    }
  };
  return (
    <div className="space-y-4">
      <p className="text-sm text-default-500">{t("cloudDownload.account.description")}</p>
      <DependencyComponent id={BAIDU_COMPONENT_ID} />
      <ProxyField thirdPartyId={ThirdPartyId.BaiduNetdisk} />
      <DownloadToolProxies toolIds={["baidu-pcs-go"]} />
      {!accounts && !error && <Spinner size="sm" />}
      {accounts?.map((account) => (
        <div
          key={account.id}
          className="flex flex-wrap items-center gap-2 rounded-lg bg-default-100 p-3"
        >
          <div className="min-w-32 flex-1">
            <p className="text-sm font-medium">{account.name}</p>
            <p className="text-xs text-default-500">
              {t(
                account.isLoggedIn
                  ? "cloudDownload.account.loggedIn"
                  : "cloudDownload.account.loggedOut",
              )}
              {account.uid && ` · ${account.uid}`}
              {account.lastValidatedAt &&
                ` · ${new Date(account.lastValidatedAt).toLocaleString()}`}
            </p>
          </div>
          <Switch
            size="sm"
            isDisabled={busy || !account.isLoggedIn}
            isSelected={account.isDefault}
            onValueChange={() =>
              void perform(() => BApi.baiduPcs.setDefaultBaiduPcsAccount(account.id))
            }
          >
            {t("cloudDownload.account.default")}
          </Switch>
          <Button
            size="sm"
            variant="light"
            isDisabled={busy || !account.isLoggedIn}
            onPress={() => void perform(() => BApi.baiduPcs.validateBaiduPcsAccount(account.id))}
          >
            {t("cloudDownload.account.validate")}
          </Button>
          <Button
            size="sm"
            variant="light"
            isDisabled={busy}
            onPress={() => {
              setEditingId(account.id);
              setName(account.name);
              setBduss("");
              setStoken("");
              setCookies("");
            }}
          >
            {t("cloudDownload.account.login")}
          </Button>
          <Button
            size="sm"
            variant="light"
            isDisabled={busy || !account.isLoggedIn}
            onPress={() => void perform(() => BApi.baiduPcs.logoutBaiduPcsAccount(account.id))}
          >
            {t("cloudDownload.account.logout")}
          </Button>
          <Button
            size="sm"
            variant="light"
            color="danger"
            isDisabled={busy}
            onPress={() => void perform(() => BApi.baiduPcs.deleteBaiduPcsAccount(account.id))}
          >
            {t("common.action.delete")}
          </Button>
          <div className="w-full">
            <BaiduDownloadSettings
              account={account}
              busy={busy}
              onRefresh={() =>
                void perform(() => BApi.baiduPcs.refreshBaiduPcsMembership(account.id))
              }
              onSave={(settings) =>
                void perform(() => BApi.baiduPcs.setBaiduPcsDownloadOptions(account.id, settings))
              }
            />
          </div>
        </div>
      ))}
      <div className="space-y-3 rounded-lg border border-default-200 p-3">
        <p className="text-sm font-medium">
          {t(editingId ? "cloudDownload.account.login" : "cloudDownload.account.add")}
        </p>
        <Input label={t("cloudDownload.account.name")} value={name} onValueChange={setName} />
        <Switch size="sm" isSelected={cookieMode} onValueChange={setCookieMode}>
          {t("cloudDownload.account.cookieMode")}
        </Switch>
        {cookieMode ? (
          <Input
            label="Cookie"
            type="password"
            autoComplete="off"
            description={t("cloudDownload.account.cookieHint")}
            value={cookies}
            onValueChange={setCookies}
          />
        ) : (
          <>
            <Input
              label="BDUSS"
              type="password"
              autoComplete="off"
              value={bduss}
              onValueChange={setBduss}
            />
            <Input
              label="STOKEN"
              type="password"
              autoComplete="off"
              description={t("cloudDownload.account.stoken")}
              value={stoken}
              onValueChange={setStoken}
            />
          </>
        )}
        <ExternalLink
          href="https://github.com/qjfoidnh/BaiduPCS-Go/blob/v4.0.2/README.md#登录百度帐号"
          size="sm"
        >
          {t("cloudDownload.account.loginGuide")}
        </ExternalLink>
        <div className="flex gap-2">
          <Button
            size="sm"
            color="primary"
            isLoading={busy}
            isDisabled={
              !name.trim() || (cookieMode ? !cookies.trim() : !bduss.trim() || !stoken.trim())
            }
            onPress={() =>
              void perform(async () => {
                const response = await BApi.baiduPcs.loginBaiduPcsAccount({
                  id: editingId,
                  name: name.trim(),
                  ...(cookieMode
                    ? { cookies: cookies.trim() }
                    : { bduss: bduss.trim(), stoken: stoken.trim() }),
                });
                if (!response.code) {
                  setBduss("");
                  setStoken("");
                  setCookies("");
                  setName("");
                  setEditingId(undefined);
                }
                return response;
              })
            }
          >
            {t("cloudDownload.account.login")}
          </Button>
          {editingId && (
            <Button
              size="sm"
              variant="light"
              onPress={() => {
                setEditingId(undefined);
                setName("");
                setBduss("");
                setStoken("");
                setCookies("");
              }}
            >
              {t("common.action.cancel")}
            </Button>
          )}
        </div>
      </div>
      {error && (
        <p role="alert" className="text-sm text-danger">
          {t(`cloudDownload.problem.${error}`, { defaultValue: error })}
        </p>
      )}
    </div>
  );
}
