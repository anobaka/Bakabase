import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Button, Chip, Input, Modal, Spinner } from "@/components/bakaui";
import ExternalLink from "@/components/ExternalLink";
import { openExternalUrl } from "@/utils/openExternalUrl";
import { extractErrorMessage } from "@/components/utils";
import BApi from "@/sdk/BApi";
import { downloadProblemLabel } from "./problemLabels";
import { DownloadMethodAvailabilityNotice } from "./DownloadMethodAvailability";

type Account = NonNullable<Awaited<ReturnType<typeof BApi.terabox.getTeraBoxAccount>>["data"]>;
type Session = NonNullable<
  Awaited<ReturnType<typeof BApi.terabox.startTeraBoxAuthorization>>["data"]
>;
const privateRequest = { showErrorToast: false, sensitiveResponse: true };
const terminalStatuses = new Set(["authorized", "cancelled", "expired", "failed"]);

function checked<T extends { code?: number; message?: string | null }>(response: T): T {
  if (response.code) throw new Error(response.message || "TERABOX_REQUEST_FAILED");
  return response;
}

/** The server owns the session and tokens; the one-time authorization code is never persisted in the browser. */
export default function TeraBoxCliAccountsPanel({
  onChanged,
  showMethodAvailability = true,
}: { onChanged?: () => void; showMethodAvailability?: boolean } = {}) {
  const { t } = useTranslation();
  const [account, setAccount] = useState<Account>();
  const [session, setSession] = useState<Session>();
  const [code, setCode] = useState("");
  const [error, setError] = useState<string>();
  const [busy, setBusy] = useState(false);
  const [ready, setReady] = useState(false);
  const [needsRefresh, setNeedsRefresh] = useState(false);
  const [disconnectOpen, setDisconnectOpen] = useState(false);
  const generation = useRef(0);
  const locked = useRef(false);
  const mounted = useRef(false);
  const changed = useRef(onChanged);
  changed.current = onChanged;
  const isCurrent = useCallback(
    (current: number) => mounted.current && generation.current === current,
    [],
  );

  const restore = useCallback(
    async (current: number) => {
      // GET only. Navigating back must restore, never replace, an unfinished authorization.
      // Reading the active session can reconcile a completed exchange. Read the
      // account afterwards so its state reflects that reconciliation.
      const sessionResponse = checked(
        await BApi.terabox.getActiveTeraBoxAuthorization(privateRequest),
      );
      const accountResponse = checked(await BApi.terabox.getTeraBoxAccount(privateRequest));
      if (!isCurrent(current)) return;
      setAccount(accountResponse.data ?? undefined);
      setSession(sessionResponse.data ?? undefined);
      setCode("");
      setReady(true);
      setNeedsRefresh(false);
    },
    [isCurrent],
  );

  useEffect(() => {
    mounted.current = true;
    const current = ++generation.current;
    void restore(current).catch((e: unknown) => {
      if (isCurrent(current)) setError(extractErrorMessage(e));
    });
    return () => {
      mounted.current = false;
      generation.current++;
    };
  }, [restore, isCurrent]);

  const updateSession = async (next: Session | undefined | null, current: number) => {
    if (!isCurrent(current)) return;
    if (!next) throw new Error("TERABOX_AUTHORIZATION_FAILED");
    if (next.status === "authorized") {
      const connected = checked(await BApi.terabox.getTeraBoxAccount(privateRequest)).data;
      if (!isCurrent(current)) return;
      if (!connected?.isConfigured) throw new Error("TERABOX_ACCOUNT_REQUIRED");
      setAccount(connected);
      changed.current?.();
    }
    if (isCurrent(current)) setSession(next);
  };

  const perform = async (operation: (current: number) => Promise<void>, mutation = true) => {
    if (locked.current) return;
    locked.current = true;
    setBusy(true);
    setError(undefined);
    const current = ++generation.current;
    try {
      await operation(current);
    } catch (e) {
      if (isCurrent(current)) {
        setError(extractErrorMessage(e));
        // A lost response can follow a successful token exchange. Read the saved state
        // before another mutation; never automatically resubmit a one-time code.
        if (mutation) setNeedsRefresh(true);
      }
    } finally {
      if (isCurrent(current)) {
        locked.current = false;
        setBusy(false);
      }
    }
  };

  const pending = session?.status === "pending";
  const disabled = busy || !ready || needsRefresh;
  const codeDisabled = disabled || (pending && !!session.problem);
  const problem =
    error ||
    session?.problem ||
    (!pending ? account?.problem : undefined) ||
    (session?.status === "authorized" && !account?.isConfigured
      ? "TERABOX_ACCOUNT_REQUIRED"
      : undefined);
  return (
    <section
      className="space-y-3 rounded-lg border border-default-200 p-3"
      aria-label={t("terabox.account.title")}
    >
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-medium">{t("terabox.account.title")}</h3>
        {account && (
          <Chip size="sm" variant="flat" color={account.isConfigured ? "success" : "default"}>
            {t(account.isConfigured ? "terabox.account.connected" : "terabox.account.notConnected")}
          </Chip>
        )}
      </div>
      {showMethodAvailability && <DownloadMethodAvailabilityNotice methodId="terabox.cli" />}
      <p className="text-xs leading-relaxed text-default-500">{t("terabox.account.description")}</p>
      {!account && !error && <Spinner size="sm" />}
      {account?.displayName && <p className="text-sm">{account.displayName}</p>}
      <p className="text-xs leading-relaxed text-default-500">{t("terabox.account.permissions")}</p>
      {problem && (
        <p role="alert" className="break-words text-sm text-warning-600">
          {downloadProblemLabel(problem, t)}
        </p>
      )}
      {needsRefresh && (
        <p role="status" className="text-xs text-warning-600">
          {t("terabox.authorization.refreshRequired")}
        </p>
      )}
      {pending && (
        <div
          className="space-y-3 rounded-lg bg-default-100 p-3"
          role="region"
          aria-label={t("terabox.authorization.title")}
        >
          <p className="text-sm">{t("terabox.authorization.instructions")}</p>
          <Button
            size="sm"
            color="primary"
            isDisabled={busy || !session.authorizationUri}
            onPress={() => {
              // Keep browser opening synchronous with the click, including on remote devices.
              if (session.authorizationUri) openExternalUrl(session.authorizationUri);
            }}
          >
            {t("terabox.authorization.open")}
          </Button>
          <Input
            label={t("terabox.authorization.code")}
            description={t("terabox.authorization.codeHint")}
            value={code}
            onValueChange={setCode}
            type="password"
            autoComplete="off"
            isDisabled={codeDisabled}
          />
          <div className="flex flex-wrap gap-2">
            <Button
              size="sm"
              color="primary"
              isLoading={busy}
              isDisabled={codeDisabled || !code.trim()}
              onPress={() => {
                const submittedCode = code.trim();
                setCode("");
                void perform(async (current) => {
                  await updateSession(
                    checked(
                      await BApi.terabox.completeTeraBoxAuthorization(
                        session.id,
                        { code: submittedCode },
                        privateRequest,
                      ),
                    ).data,
                    current,
                  );
                });
              }}
            >
              {t("terabox.authorization.complete")}
            </Button>
            <Button
              size="sm"
              variant="light"
              isDisabled={disabled}
              onPress={() =>
                void perform(async (current) => {
                  checked(
                    await BApi.terabox.cancelTeraBoxAuthorization(session.id, privateRequest),
                  );
                  if (isCurrent(current)) {
                    setSession(undefined);
                    setCode("");
                  }
                })
              }
            >
              {t("terabox.authorization.cancel")}
            </Button>
          </div>
          <p className="text-xs text-default-500">{t("terabox.authorization.noCallback")}</p>
          {session.problem && (
            <p role="status" className="text-xs text-warning-600">
              {t("terabox.authorization.codeSubmitted")}
            </p>
          )}
        </div>
      )}
      {session &&
        terminalStatuses.has(session.status) &&
        (session.status !== "authorized" || account?.isConfigured) && (
          <p role="status" className="text-sm text-default-600">
            {t(`terabox.authorization.status.${session.status}`)}
          </p>
        )}
      <div className="flex flex-wrap items-center gap-2">
        {!pending && (
          <Button
            size="sm"
            color="primary"
            isLoading={busy}
            isDisabled={disabled}
            onPress={() =>
              void perform(async (current) => {
                await updateSession(
                  checked(await BApi.terabox.startTeraBoxAuthorization(privateRequest)).data,
                  current,
                );
              })
            }
          >
            {t(account?.isConfigured ? "terabox.account.reauthorize" : "terabox.account.authorize")}
          </Button>
        )}
        <Button
          size="sm"
          variant="light"
          isDisabled={busy}
          onPress={() =>
            void perform(async (current) => {
              await restore(current);
              if (isCurrent(current)) changed.current?.();
            }, false)
          }
        >
          {t("terabox.account.refresh")}
        </Button>
        {account?.isConfigured && !pending && (
          <Button
            size="sm"
            variant="light"
            color="danger"
            isDisabled={disabled}
            onPress={() => setDisconnectOpen(true)}
          >
            {t("terabox.account.disconnect")}
          </Button>
        )}
      </div>
      <ExternalLink href="https://github.com/terabox-storage/skills" size="sm">
        {t("terabox.account.documentation")}
      </ExternalLink>
      {disconnectOpen && (
        <Modal
          defaultVisible
          size="lg"
          title={t("terabox.account.disconnect")}
          onClose={() => setDisconnectOpen(false)}
          footer={
            <div className="flex gap-2">
              <Button variant="light" isDisabled={busy} onPress={() => setDisconnectOpen(false)}>
                {t("common.action.close")}
              </Button>
              <Button
                color="danger"
                isLoading={busy}
                isDisabled={disabled}
                onPress={() =>
                  void perform(async (current) => {
                    checked(await BApi.terabox.logoutTeraBoxAccount(privateRequest));
                    await restore(current);
                    if (isCurrent(current)) {
                      setDisconnectOpen(false);
                      changed.current?.();
                    }
                  })
                }
              >
                {t("terabox.account.disconnect")}
              </Button>
            </div>
          }
        >
          <p className="text-sm">{t("terabox.account.disconnectHint")}</p>
        </Modal>
      )}
    </section>
  );
}
