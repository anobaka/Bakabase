import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Chip, Modal, Radio, RadioGroup, Spinner } from "@/components/bakaui";
import PikPakAccountCapacity from "./PikPakAccountCapacity";
import ExternalLink from "@/components/ExternalLink";
import { openExternalUrl } from "@/utils/openExternalUrl";
import { extractErrorMessage, humanFileSize } from "@/components/utils";
import BApi from "@/sdk/BApi";
import { downloadProblemLabel } from "./problemLabels";

type Account = NonNullable<Awaited<ReturnType<typeof BApi.pikpakCli.getPikPakCliAccount>>["data"]>;
type Session = NonNullable<
  Awaited<ReturnType<typeof BApi.pikpakCli.startPikPakCliAuthorization>>["data"]
>;
const privateRequest = { showErrorToast: false, sensitiveResponse: true };

function checked<T extends { code?: number; message?: string | null }>(response: T): T {
  if (response.code) throw new Error(response.message || "PIKPAK_CLI_REQUEST_FAILED");
  return response;
}

/** Only the short-lived user code reaches the browser; device codes and credentials stay on the server. */
export default function PikPakCliAccountsPanel({ onChanged }: { onChanged?: () => void } = {}) {
  const { t } = useTranslation();
  const [account, setAccount] = useState<Account>();
  const [session, setSession] = useState<Session>();
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [ready, setReady] = useState(false);
  const [disconnectOpen, setDisconnectOpen] = useState(false);
  const generation = useRef(0);
  const locked = useRef(false);
  const mounted = useRef(false);
  const changed = useRef(onChanged);
  changed.current = onChanged;

  const load = useCallback(async (current: number) => {
    const response = checked(await BApi.pikpakCli.getPikPakCliAccount(privateRequest));
    if (mounted.current && generation.current === current) setAccount(response.data ?? undefined);
    return response.data;
  }, []);

  const restore = useCallback(async (current: number) => {
    // The server owns authorization sessions. Navigation must not lose an approved
    // device challenge or implicitly replace it with a new authorization.
    const [accountResponse, sessionResponse] = await Promise.all([
      BApi.pikpakCli.getPikPakCliAccount(privateRequest).then(checked),
      BApi.pikpakCli.getActivePikPakCliAuthorization(privateRequest).then(checked),
    ]);
    if (!mounted.current || generation.current !== current) return;
    setAccount(accountResponse.data ?? undefined);
    setSession(sessionResponse.data ?? undefined);
    setReady(true);
  }, []);

  useEffect(() => {
    mounted.current = true;
    const current = ++generation.current;
    void restore(current).catch((e: unknown) => {
      if (mounted.current && generation.current === current) setError(extractErrorMessage(e));
    });
    return () => {
      mounted.current = false;
      generation.current++;
    };
  }, [restore]);

  const updateSession = useCallback(
    async (next: Session | undefined, current: number) => {
      if (!mounted.current || generation.current !== current) return;
      if (!next) throw new Error("PIKPAK_AUTHORIZATION_FAILED");
      if (next.status === "authorized") {
        const connected = await load(current);
        if (!mounted.current || generation.current !== current) return;
        if (!connected?.isConfigured) throw new Error("PIKPAK_ACCOUNT_REQUIRED");
        setSession(next);
        changed.current?.();
      } else {
        setSession(next);
      }
    },
    [load],
  );

  const perform = async (operation: (current: number) => Promise<void>) => {
    if (locked.current) return;
    locked.current = true;
    setBusy(true);
    setError(undefined);
    const current = ++generation.current;
    try {
      await operation(current);
    } catch (e) {
      if (mounted.current && generation.current === current) setError(extractErrorMessage(e));
    } finally {
      if (mounted.current && generation.current === current) {
        locked.current = false;
        setBusy(false);
      }
    }
  };

  useEffect(() => {
    if (busy || error || session?.status !== "pending") return;
    let active = true;
    const current = generation.current;
    const sessionId = session.id;
    let timer: ReturnType<typeof setTimeout>;
    const isCurrent = () => active && mounted.current && generation.current === current;
    const poll = async () => {
      try {
        const next = checked(
          await BApi.pikpakCli.pollPikPakCliAuthorization(sessionId, privateRequest),
        ).data;
        if (!isCurrent()) return;
        await updateSession(next ?? undefined, current);
        if (isCurrent() && next?.status === "pending")
          timer = setTimeout(poll, Math.max(2, next.intervalSeconds || 5) * 1000);
      } catch (e) {
        // An explicit refresh retries a network failure. Never keep polling a failing endpoint.
        if (isCurrent()) setError(extractErrorMessage(e));
      }
    };
    timer = setTimeout(poll, Math.max(2, session.intervalSeconds || 5) * 1000);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [session?.id, session?.status, busy, error, updateSession]);

  const authorizeCleanup = (permanent: boolean) => {
    void perform(async (current) => {
      const request = permanent
        ? BApi.pikpakCli.startPikPakCliPurgeFilesAuthorization
        : BApi.pikpakCli.startPikPakCliManageFilesAuthorization;
      await updateSession(checked(await request(privateRequest)).data ?? undefined, current);
    });
  };

  const pending = session?.status === "pending";
  const problem = error || session?.problem || (!pending ? account?.problem : undefined);
  return (
    <section
      className="space-y-3 rounded-lg border border-default-200 p-3"
      aria-label={t("pikpakCli.account.title")}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-medium">{t("pikpakCli.account.title")}</h3>
        {account && (
          <Chip size="sm" variant="flat" color={account.isConfigured ? "success" : "default"}>
            {t(
              account.isConfigured
                ? "pikpakCli.account.connected"
                : "pikpakCli.account.notConnected",
            )}
          </Chip>
        )}
      </div>
      <p className="text-xs leading-relaxed text-default-500">
        {t("pikpakCli.account.description")}
      </p>
      {!account && !error && <Spinner size="sm" />}
      {account?.displayName && <p className="text-sm">{account.displayName}</p>}
      {account?.isConfigured && account.accountId && account.accountVersion && (
        <PikPakAccountCapacity
          key={JSON.stringify([account.accountId, account.accountVersion])}
          accountId={account.accountId}
          accountVersion={account.accountVersion}
          savedQuota={account.quota}
        />
      )}
      {account?.quota?.remainingTransferBytes != null && (
        <p className="text-xs text-default-500">
          {t("pikpakCli.account.transferRemaining", {
            size: humanFileSize(account.quota.remainingTransferBytes),
          })}
        </p>
      )}
      <p className="text-xs leading-relaxed text-default-500">
        {t("pikpakCli.account.permissions")}
      </p>
      {account?.isConfigured && !pending && (
        <section className="space-y-2" aria-label={t("pikpakCli.cleanup.title")}>
          <RadioGroup
            label={t("pikpakCli.cleanup.afterVerification")}
            value={
              account.permanentlyDeleteTemporaryCopiesAfterDownload ? "delete"
                : account.trashTemporaryCopiesAfterDownload ? "trash" : "keep"
            }
            isDisabled={busy || !ready || !account.accountId || !account.accountVersion}
            onValueChange={(mode) => {
              const { accountId, accountVersion } = account;
              if (!accountId || !accountVersion) return;
              void perform(async (current) => {
                const updated = checked(
                  await BApi.pikpakCli.setPikPakCliCleanupSettings(
                    {
                      expectedAccountId: accountId,
                      expectedAccountVersion: accountVersion,
                      trashTemporaryCopiesAfterDownload: mode !== "keep",
                      permanentlyDeleteTemporaryCopiesAfterDownload: mode === "delete",
                    },
                    privateRequest,
                  ),
                ).data;
                if (!updated) throw new Error("PIKPAK_CLI_REQUEST_FAILED");
                if (mounted.current && generation.current === current) {
                  setAccount(updated);
                  changed.current?.();
                }
              });
            }}
          >
            <Radio value="keep">{t("pikpakCli.cleanup.keep")}</Radio>
            <Radio value="trash" isDisabled={!account.canManageFiles}>{t("pikpakCli.cleanup.trash")}</Radio>
            <Radio value="delete" isDisabled={!account.canPermanentlyDeleteFiles}>{t("pikpakCli.cleanup.delete")}</Radio>
          </RadioGroup>
          <p className="text-xs leading-relaxed text-default-500">{t("pikpakCli.cleanup.hint")}</p>
          {account.accountId && account.accountVersion ? (
            <div className="flex flex-wrap gap-2">
              {!account.canManageFiles && (
                <Button
                  size="sm"
                  variant="flat"
                  isDisabled={busy || !ready}
                  onPress={() => authorizeCleanup(false)}
                >
                  {t("pikpakCli.cleanup.authorize")}
                </Button>
              )}
              {!account.canPermanentlyDeleteFiles && (
                <Button
                  size="sm"
                  variant="flat"
                  isDisabled={busy || !ready}
                  onPress={() => authorizeCleanup(true)}
                >
                  {t("pikpakCli.cleanup.authorizeDeletion")}
                </Button>
              )}
            </div>
          ) : (
            <p className="text-xs text-warning-600">
              {t("cloudDownload.problemCode.PIKPAK_ACCOUNT_IDENTITY_UNVERIFIED")}
            </p>
          )}
        </section>
      )}
      {problem && (
        <p role="alert" className="break-words text-sm text-warning-600">
          {downloadProblemLabel(problem, t)}
        </p>
      )}
      {pending && (
        <div
          className="space-y-3 rounded-lg bg-default-100 p-3"
          role="region"
          aria-label={t("pikpakCli.authorization.title")}
        >
          <p className="text-sm">{t("pikpakCli.authorization.instructions")}</p>
          {session.userCode && (
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="text-default-500">{t("pikpakCli.authorization.code")}</span>
              <code className="select-all rounded bg-default-200 px-2 py-1 font-mono tracking-widest">
                {session.userCode}
              </code>
            </div>
          )}
          <div className="flex flex-wrap items-center gap-2">
            <Button
              size="sm"
              color="primary"
              isDisabled={!session.verificationUri}
              onPress={() => {
                // Opening remains synchronous with the user's click, including in remote browsers.
                if (session.verificationUri) openExternalUrl(session.verificationUri);
              }}
            >
              {t("pikpakCli.authorization.open")}
            </Button>
            <Button
              size="sm"
              variant="light"
              isDisabled={busy}
              onPress={() =>
                void perform(async (current) => {
                  await updateSession(
                    checked(
                      await BApi.pikpakCli.pollPikPakCliAuthorization(session.id, privateRequest),
                    ).data ?? undefined,
                    current,
                  );
                })
              }
            >
              {t("pikpakCli.authorization.refresh")}
            </Button>
            <Button
              size="sm"
              variant="light"
              isDisabled={busy}
              onPress={() =>
                void perform(async (current) => {
                  checked(
                    await BApi.pikpakCli.cancelPikPakCliAuthorization(session.id, privateRequest),
                  );
                  if (mounted.current && generation.current === current) setSession(undefined);
                })
              }
            >
              {t("pikpakCli.authorization.cancel")}
            </Button>
          </div>
          <p role="status" className="text-xs text-default-500">
            {t(error ? "pikpakCli.authorization.pollingPaused" : "pikpakCli.authorization.waiting")}
          </p>
        </div>
      )}
      {session && !pending && (
        <p role="status" className="text-sm text-default-600">
          {t(`pikpakCli.authorization.status.${session.status}`)}
        </p>
      )}
      {!pending && (
        <div className="flex flex-wrap items-center gap-2">
          <Button
            size="sm"
            color="primary"
            isLoading={busy}
            isDisabled={!ready}
            onPress={() =>
              void perform(async (current) => {
                await updateSession(
                  checked(await BApi.pikpakCli.startPikPakCliAuthorization(privateRequest)).data ??
                    undefined,
                  current,
                );
              })
            }
          >
            {t(
              account?.isConfigured
                ? "pikpakCli.account.reauthorize"
                : "pikpakCli.account.authorize",
            )}
          </Button>
          <Button size="sm" variant="light" isDisabled={busy} onPress={() => void perform(restore)}>
            {t("cloudDownload.account.refresh")}
          </Button>
          {account?.isConfigured && (
            <Button
              size="sm"
              variant="light"
              color="danger"
              isDisabled={busy}
              onPress={() => setDisconnectOpen(true)}
            >
              {t("pikpakCli.account.disconnect")}
            </Button>
          )}
        </div>
      )}
      <p className="text-xs leading-relaxed text-default-500">{t("pikpakCli.account.quotaHint")}</p>
      <ExternalLink href="https://mypikpak.com/en-US/cli" size="sm">
        {t("pikpakCli.account.documentation")}
      </ExternalLink>
      {disconnectOpen && (
        <Modal
          defaultVisible
          size="lg"
          title={t("pikpakCli.account.disconnect")}
          onClose={() => setDisconnectOpen(false)}
          footer={
            <div className="flex gap-2">
              <Button variant="light" isDisabled={busy} onPress={() => setDisconnectOpen(false)}>
                {t("common.action.close")}
              </Button>
              <Button
                color="danger"
                isLoading={busy}
                onPress={() =>
                  void perform(async (current) => {
                    checked(await BApi.pikpakCli.logoutPikPakCliAccount(privateRequest));
                    await load(current);
                    if (mounted.current && generation.current === current) {
                      setSession(undefined);
                      setDisconnectOpen(false);
                      changed.current?.();
                    }
                  })
                }
              >
                {t("pikpakCli.account.disconnect")}
              </Button>
            </div>
          }
        >
          <p className="text-sm">{t("pikpakCli.account.disconnectHint")}</p>
        </Modal>
      )}
    </section>
  );
}
