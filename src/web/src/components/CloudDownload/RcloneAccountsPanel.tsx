import { useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import {
  Accordion,
  AccordionItem,
  Button,
  Input,
  Select,
  Spinner,
  Switch,
} from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import RcloneBrowserAuthorization from "./RcloneBrowserAuthorization";
import { isRcloneDriveQuestion, presentRcloneAnswerOptions } from "./rcloneQuestionPresentation";
import RcloneDriveSelection from "./RcloneDriveSelection";
import OAuthClientConfiguration, {
  type OAuthConfigurationStatus,
} from "@/components/OAuthClientConfiguration";
import type {
  BakabaseServiceComponentsCloudDownloadsRcloneRcloneAccountViewModel as Account,
  BakabaseServiceComponentsCloudDownloadsRcloneRcloneAuthorizationSessionViewModel as Session,
} from "@/sdk/Api";

const authorizationRequestOptions = { showErrorToast: false, sensitiveResponse: true };

/** Account configuration and authorization are explicit user actions. Secrets are write-only. */
export default function RcloneAccountsPanel({
  providerId,
  onChanged,
}: {
  providerId: string;
  onChanged?: () => void;
}) {
  const { t } = useTranslation();
  const [accounts, setAccounts] = useState<Account[]>();
  const [editingId, setEditingId] = useState<string>();
  const [name, setName] = useState("");
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [rootFolderId, setRootFolderId] = useState(providerId === "pixeldrain" ? "me" : "");
  const [session, setSession] = useState<Session>();
  const [answer, setAnswer] = useState("");
  const [manualAnswer, setManualAnswer] = useState(false);
  const [busy, setBusy] = useState(false);
  const [cancelling, setCancelling] = useState(false);
  const [error, setError] = useState<string>();
  const [oauthConfiguration, setOAuthConfiguration] = useState<OAuthConfigurationStatus>();
  const locked = useRef(false);
  const cancellationPending = useRef(false);
  const generation = useRef(0);
  const ownedSession = useRef<string>();
  const currentQuestion = useRef<string>();
  const api = BApi.rclone;
  const check = <T extends { code?: number; message?: string | null }>(response: T) => {
    if (response.code) throw new Error(response.message || "RCLONE_REQUEST_FAILED");
    return response;
  };
  const problemLabel = (message: string) => {
    const code = message.match(/\b(?:RCLONE|OAUTH)_[A-Z][A-Z0-9_]*\b/)?.[0];
    return code
      ? t(`${code.startsWith("OAUTH_") ? "oauth" : "rclone"}.problem.${code}`, {
          defaultValue: message,
        })
      : message;
  };
  const load = async () => {
    const current = generation.current;
    const response = check(await api.getRcloneAccounts({ providerId }));
    if (current === generation.current) setAccounts(response.data ?? []);
  };
  const cancelOwnedSession = (id: string) => {
    // Only sessions started by this mounted panel are ever passed here.
    void api.cancelRcloneAuthorization(id, authorizationRequestOptions).catch(() => undefined);
  };
  useEffect(() => {
    const current = ++generation.current;
    locked.current = false;
    cancellationPending.current = false;
    setBusy(false);
    setCancelling(false);
    setAccounts(undefined);
    setSession(undefined);
    currentQuestion.current = undefined;
    setAnswer("");
    setManualAnswer(false);
    setEditingId(undefined);
    setName("");
    setUsername("");
    setPassword("");
    setApiKey("");
    setRootFolderId(providerId === "pixeldrain" ? "me" : "");
    setError(undefined);
    setOAuthConfiguration(undefined);
    void load().catch((e: unknown) => {
      if (current === generation.current) setError(e instanceof Error ? e.message : String(e));
    });
    return () => {
      generation.current++;
      const id = ownedSession.current;
      ownedSession.current = undefined;
      if (id) cancelOwnedSession(id);
    };
  }, [providerId]);
  const perform = async (operation: (isCurrent: () => boolean) => Promise<void>) => {
    if (locked.current) return false;
    const current = generation.current;
    const isCurrent = () => current === generation.current;
    locked.current = true;
    setBusy(true);
    setError(undefined);
    try {
      await operation(isCurrent);
      return isCurrent();
    } catch (e) {
      if (isCurrent()) setError(e instanceof Error ? e.message : String(e));
      return false;
    } finally {
      if (isCurrent()) {
        locked.current = false;
        setBusy(false);
      }
    }
  };
  const reloadAndNotify = async (isCurrent: () => boolean) => {
    if (!isCurrent()) return;
    await load();
    if (isCurrent()) onChanged?.();
  };
  const updateSession = async (
    next: Session | undefined,
    isCurrent: () => boolean,
    startBrowser = false,
  ) => {
    if (!isCurrent()) {
      // A session can be created after navigation already cleaned up the previous one.
      if (next?.status === "awaitingInput" && ownedSession.current !== next.id)
        cancelOwnedSession(next.id);
      return;
    }
    if (!next) throw new Error("RCLONE_AUTHORIZATION_FAILED");
    ownedSession.current = next.status === "awaitingInput" ? next.id : undefined;
    setSession(next);
    const questionKey = JSON.stringify([next.id, next.question?.id ?? ""]);
    if (currentQuestion.current !== questionKey) {
      currentQuestion.current = questionKey;
      const defaultValue = next.question?.isSecret ? "" : (next.question?.defaultValue ?? "");
      const listedDriveDefault =
        next.question &&
        isRcloneDriveQuestion(next.question) &&
        next.question.examples.some((example) => example.value === defaultValue);
      // A native drive default is not evidence that this is the user's accessible drive.
      setAnswer(listedDriveDefault ? "" : defaultValue);
      // Examples are suggestions unless exclusive. Keep an unlisted default available
      // as a manual value instead of silently replacing it with the first suggestion.
      setManualAnswer(
        !!next.question &&
          !next.question.isSecret &&
          !next.question.exclusive &&
          next.question.examples.length > 0 &&
          !!next.question.defaultValue &&
          !next.question.examples.some((example) => example.value === next.question?.defaultValue),
      );
    }
    if (startBrowser && next.status === "awaitingInput" && next.question?.name === "config_token") {
      await updateSession(
        check(
          await api.startRcloneBrowserAuthorization(
            next.id,
            { questionId: next.question.id },
            authorizationRequestOptions,
          ),
        ).data,
        isCurrent,
      );
    }
    if (next.status === "ready") {
      await reloadAndNotify(isCurrent);
    }
  };
  const question = session?.question;
  const awaiting = session?.status === "awaitingInput";
  useEffect(() => {
    if (
      busy ||
      cancelling ||
      !session ||
      session.authorizationMode !== "automatic" ||
      session.status !== "awaitingInput" ||
      session.question?.name !== "config_token" ||
      !["awaitingCallback", "exchanging"].includes(session.browser?.status ?? "")
    )
      return;
    const current = generation.current;
    const id = session.id;
    let active = true;
    let timer: ReturnType<typeof setTimeout>;
    const isCurrent = () => active && current === generation.current && ownedSession.current === id;
    const poll = async () => {
      if (!isCurrent()) return;
      if (locked.current) {
        timer = setTimeout(poll, 2000);
        return;
      }
      try {
        const next = check(await api.getRcloneAuthorization(id, authorizationRequestOptions)).data;
        if (!isCurrent()) return;
        await updateSession(next, isCurrent);
        if (isCurrent()) timer = setTimeout(poll, 2000);
      } catch (e) {
        // An explicit refresh retries after network errors; do not continuously hammer a failing API.
        if (isCurrent()) setError(e instanceof Error ? e.message : String(e));
      }
    };
    timer = setTimeout(poll, 2000);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [
    session?.id,
    session?.authorizationMode,
    session?.status,
    session?.question?.id,
    session?.browser?.status,
    busy,
    cancelling,
  ]);
  const canStartAuthorization =
    providerId !== "oneDrive" ||
    (!!oauthConfiguration &&
      !oauthConfiguration.hasUnsavedChanges &&
      (oauthConfiguration.mode === "manual" || oauthConfiguration.isReady));
  const cancelAuthorization = async () => {
    if (!session || cancellationPending.current) return;
    const id = session.id;
    // Cancellation must interrupt preparation/exchange, not queue behind the ordinary UI lock.
    // Invalidate every older response without resetting the current provider's account list.
    const current = ++generation.current;
    cancellationPending.current = true;
    locked.current = true;
    setCancelling(true);
    setBusy(true);
    setAnswer("");
    setError(undefined);
    try {
      check(await api.cancelRcloneAuthorization(id, authorizationRequestOptions));
      if (current !== generation.current) return;
      if (ownedSession.current === id) ownedSession.current = undefined;
      setSession(undefined);
      currentQuestion.current = undefined;
    } catch (e) {
      // Keep the session visible: a failed cancellation can be refreshed and retried.
      if (current === generation.current) setError(e instanceof Error ? e.message : String(e));
    } finally {
      if (current === generation.current) {
        cancellationPending.current = false;
        locked.current = false;
        setCancelling(false);
        setBusy(false);
      }
    }
  };
  const questionLabel = question
    ? t(`rclone.authorization.question.${question.name}`, { defaultValue: question.name })
    : "";
  const hasSuggestedAnswers = !!question && !question.isSecret && question.examples.length > 0;
  const isSuggestedAnswer = question?.examples.some((example) => example.value === answer) ?? false;
  const answerOptions = question ? presentRcloneAnswerOptions(question, t) : [];
  const selectedAnswer = answerOptions.find((option) => option.value === answer);
  const questionForm = awaiting && question && session && (
    <>
      {hasSuggestedAnswers && isRcloneDriveQuestion(question) && (
        <div hidden={manualAnswer}>
          <RcloneDriveSelection
            key={`${session.id}:${question.id}`}
            sessionId={session.id}
            question={question}
            answer={answer}
            onAnswer={setAnswer}
            isDisabled={busy}
            allowAutomaticSelection={!manualAnswer}
            problemLabel={problemLabel}
          />
        </div>
      )}
      {hasSuggestedAnswers && !manualAnswer ? (
        !isRcloneDriveQuestion(question) && (
          <Select
            label={questionLabel}
            selectedKeys={isSuggestedAnswer ? [answer] : []}
            isRequired={question.required}
            isDisabled={busy}
            dataSource={answerOptions}
            description={selectedAnswer?.description}
            listboxProps={{
              itemClasses: {
                title: "whitespace-normal",
                description: "whitespace-normal",
              },
            }}
            onSelectionChange={(keys) => setAnswer(String(Array.from(keys)[0] ?? ""))}
          />
        )
      ) : (
        <Input
          label={question.isSecret ? t("rclone.authorization.secretAnswer") : questionLabel}
          type={question.isSecret ? "password" : "text"}
          autoComplete="off"
          isRequired={question.required}
          isDisabled={busy}
          value={answer}
          onValueChange={setAnswer}
        />
      )}
      {hasSuggestedAnswers && selectedAnswer && (
        <Accordion isCompact key={`${question.id}:${answer}`}>
          <AccordionItem key="selected-answer" title={t("rclone.authorization.answerDetails")}>
            <dl className="space-y-1 break-all text-xs text-default-500">
              <dt>{t("rclone.authorization.originalAnswerLabel")}</dt>
              <dd className="whitespace-pre-wrap">{selectedAnswer.originalHelp || "—"}</dd>
              <dt>{t("rclone.authorization.originalAnswerValue")}</dt>
              <dd>{selectedAnswer.value}</dd>
            </dl>
          </AccordionItem>
        </Accordion>
      )}
      {manualAnswer && isRcloneDriveQuestion(question) && !isSuggestedAnswer && (
        <p className="text-xs text-default-500">
          {t("rclone.authorization.manualDrivePreviewUnavailable")}
        </p>
      )}
      {hasSuggestedAnswers && !question.exclusive && (
        <Button
          size="sm"
          variant="light"
          isDisabled={busy}
          onPress={() => {
            if (manualAnswer && !isSuggestedAnswer) setAnswer("");
            setManualAnswer(!manualAnswer);
          }}
        >
          {t(
            manualAnswer
              ? "rclone.authorization.useSuggestedAnswer"
              : "rclone.authorization.enterManualAnswer",
          )}
        </Button>
      )}
      <Button
        size="sm"
        color="primary"
        isDisabled={
          busy ||
          ((question.required || isRcloneDriveQuestion(question)) && !answer.trim()) ||
          (hasSuggestedAnswers && !manualAnswer && !!answer && !isSuggestedAnswer)
        }
        isLoading={busy}
        onPress={() => {
          const value = answer;
          if (question.isSecret) setAnswer("");
          void perform(async (isCurrent) =>
            updateSession(
              check(
                await api.answerRcloneAuthorization(
                  session.id,
                  {
                    questionId: question.id,
                    value,
                  },
                  authorizationRequestOptions,
                ),
              ).data,
              isCurrent,
            ),
          );
        }}
      >
        {t("rclone.authorization.answer")}
      </Button>
    </>
  );
  const clearEditor = () => {
    setEditingId(undefined);
    setName("");
    setUsername("");
    setPassword("");
    setApiKey("");
    setRootFolderId(providerId === "pixeldrain" ? "me" : "");
  };
  return (
    <div className="space-y-3">
      <p className="text-sm text-default-500">{t(`rclone.account.scope.${providerId}`)}</p>
      {providerId === "oneDrive" && (
        <div className="space-y-1 rounded-lg bg-default-50 p-3 text-xs text-default-500">
          <p>{t("rclone.authorization.readOnlyScope")}</p>
          <p>{t("rclone.authorization.accessRootHint")}</p>
          <p>{t("rclone.authorization.publicShareScope")}</p>
        </div>
      )}
      {providerId === "oneDrive" && (
        <OAuthClientConfiguration
          providerId="microsoftGraph"
          title={t("oauth.configuration.microsoftGraph.title")}
          tenantPresets={["common", "organizations", "consumers"].map((value) => ({
            value,
            label: t(`oauth.configuration.tenant.${value}`),
          }))}
          helpLinks={[
            {
              href: "https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app",
              label: t("oauth.configuration.help.registerApp"),
            },
            {
              href: "https://learn.microsoft.com/en-us/entra/identity-platform/reply-url",
              label: t("oauth.configuration.help.redirectUri"),
            },
          ]}
          isDisabled={busy || awaiting}
          onStatusChange={setOAuthConfiguration}
        />
      )}
      {providerId === "oneDrive" &&
        oauthConfiguration?.mode === "automatic" &&
        !oauthConfiguration.isReady && (
          <p className="text-sm text-warning-600">
            {t("oauth.configuration.authorizationBlocked")}
          </p>
        )}
      {!accounts && !error && <Spinner size="sm" />}
      {accounts?.map((account) => (
        <div key={account.id} className="space-y-2 rounded-lg bg-default-100 p-3">
          <div className="flex flex-wrap items-center gap-2">
            <div className="min-w-32 flex-1">
              <p className="text-sm font-medium">{account.name}</p>
              <p className="text-xs text-default-500">
                {t(
                  account.isConfigured
                    ? "rclone.account.configured"
                    : "rclone.account.notConfigured",
                )}
                {account.lastValidatedAt &&
                  ` · ${t("rclone.account.lastValidated")} ${new Date(account.lastValidatedAt).toLocaleString()}`}
              </p>
              {account.problem && (
                <p className="text-xs text-warning-600">{problemLabel(account.problem)}</p>
              )}
            </div>
            <Switch
              size="sm"
              isDisabled={busy || !account.isConfigured || awaiting}
              isSelected={account.isDefault}
              onValueChange={() =>
                void perform(async (isCurrent) => {
                  check(await api.setDefaultRcloneAccount(account.id));
                  await reloadAndNotify(isCurrent);
                })
              }
            >
              {t("cloudDownload.account.default")}
            </Switch>
            <Button
              size="sm"
              variant="light"
              isDisabled={busy || !account.isConfigured || awaiting}
              onPress={() =>
                void perform(async (isCurrent) => {
                  check(await api.validateRcloneAccount(account.id));
                  await reloadAndNotify(isCurrent);
                })
              }
            >
              {t("rclone.account.validate")}
            </Button>
            {providerId === "oneDrive" ? (
              <Button
                size="sm"
                variant="flat"
                isDisabled={busy || awaiting || !canStartAuthorization}
                onPress={() =>
                  void perform(async (isCurrent) =>
                    updateSession(
                      check(
                        await api.startRcloneAuthorization(account.id, authorizationRequestOptions),
                      ).data,
                      isCurrent,
                      true,
                    ),
                  )
                }
              >
                {t("rclone.authorization.start")}
              </Button>
            ) : (
              <Button
                size="sm"
                variant="light"
                isDisabled={busy || awaiting}
                onPress={() => {
                  clearEditor();
                  setEditingId(account.id);
                  setName(account.name);
                }}
              >
                {t("rclone.account.updateCredentials")}
              </Button>
            )}
            <Button
              size="sm"
              variant="light"
              color="danger"
              isDisabled={busy || awaiting}
              onPress={() =>
                void perform(async (isCurrent) => {
                  check(await api.deleteRcloneAccount(account.id));
                  await reloadAndNotify(isCurrent);
                })
              }
            >
              {t("common.action.delete")}
            </Button>
          </div>
        </div>
      ))}
      <div className="space-y-3 rounded-lg border border-default-200 p-3">
        <p className="text-sm font-medium">
          {t(editingId ? "rclone.account.updateCredentials" : "cloudDownload.account.add")}
        </p>
        <Input
          label={t("cloudDownload.account.name")}
          value={name}
          onValueChange={setName}
          isDisabled={busy || awaiting}
        />
        {providerId === "pikpak" && (
          <>
            <Input
              label={t("rclone.account.username")}
              value={username}
              onValueChange={setUsername}
              isDisabled={busy || awaiting}
              autoComplete="off"
            />
            <Input
              label={t("rclone.account.password")}
              type="password"
              value={password}
              onValueChange={setPassword}
              isDisabled={busy || awaiting}
              autoComplete="new-password"
            />
          </>
        )}
        {providerId === "pixeldrain" && (
          <>
            <Input
              label={t("rclone.account.apiKey")}
              type="password"
              value={apiKey}
              onValueChange={setApiKey}
              isDisabled={busy || awaiting}
              autoComplete="new-password"
            />
            <Input
              label={t("rclone.account.rootFolderId")}
              description={t("rclone.account.rootFolderHint")}
              isRequired
              value={rootFolderId}
              onValueChange={setRootFolderId}
              isDisabled={busy || awaiting}
            />
          </>
        )}
        {providerId === "oneDrive" && (
          <p className="text-xs text-default-500">{t("rclone.authorization.createHint")}</p>
        )}
        <div className="flex gap-2">
          <Button
            size="sm"
            color="primary"
            isLoading={busy}
            isDisabled={
              busy ||
              awaiting ||
              !name.trim() ||
              (providerId === "pikpak" && (!username.trim() || !password)) ||
              (providerId === "pixeldrain" &&
                (!rootFolderId.trim() || (rootFolderId.trim() === "me" && !apiKey.trim())))
            }
            onPress={() =>
              void perform(async (isCurrent) => {
                check(
                  await api.saveRcloneAccount({
                    id: editingId,
                    providerId,
                    name: name.trim(),
                    username: username.trim() || undefined,
                    password: password || undefined,
                    apiKey: apiKey.trim() || undefined,
                    rootFolderId: rootFolderId.trim() || undefined,
                  }),
                );
                if (!isCurrent()) return;
                clearEditor();
                await reloadAndNotify(isCurrent);
              })
            }
          >
            {t("rclone.account.save")}
          </Button>
          {editingId && (
            <Button size="sm" variant="light" isDisabled={busy} onPress={clearEditor}>
              {t("common.action.cancel")}
            </Button>
          )}
        </div>
      </div>
      {session && (
        <div
          className="space-y-3 rounded-lg border border-primary-200 p-3"
          role="region"
          aria-label={t("rclone.authorization.title")}
        >
          <p className="text-sm">
            {t(`rclone.authorization.status.${session.status}`, { defaultValue: session.status })}
          </p>
          {awaiting && (
            <p className="text-xs text-default-500">
              {t("rclone.authorization.expires", {
                time: new Date(session.expiresAt).toLocaleString(),
              })}
            </p>
          )}
          {session.problem && (
            <p role="alert" className="text-sm text-warning-600">
              {problemLabel(session.problem)}
            </p>
          )}
          {awaiting && question?.name === "config_token" ? (
            <RcloneBrowserAuthorization
              key={`${session.id}:${question.id}:${generation.current}`}
              session={session}
              busy={busy}
              problemLabel={problemLabel}
              onStart={() =>
                perform(async (isCurrent) => {
                  setAnswer("");
                  if (session.browser) {
                    check(
                      await api.stopRcloneBrowserAuthorization(
                        session.id,
                        authorizationRequestOptions,
                      ),
                    );
                    if (!isCurrent()) return;
                  }
                  await updateSession(
                    check(
                      await api.startRcloneBrowserAuthorization(
                        session.id,
                        { questionId: question.id },
                        authorizationRequestOptions,
                      ),
                    ).data,
                    isCurrent,
                  );
                })
              }
              onStop={() =>
                perform(async (isCurrent) => {
                  setAnswer("");
                  await updateSession(
                    check(
                      await api.stopRcloneBrowserAuthorization(
                        session.id,
                        authorizationRequestOptions,
                      ),
                    ).data,
                    isCurrent,
                  );
                })
              }
              onSubmit={(callbackUrl) =>
                perform(async (isCurrent) => {
                  await updateSession(
                    check(
                      await api.submitRcloneBrowserAuthorizationCallback(
                        session.id,
                        {
                          questionId: question.id,
                          callbackUrl,
                        },
                        authorizationRequestOptions,
                      ),
                    ).data,
                    isCurrent,
                  );
                })
              }
            >
              <p className="text-sm">{t("rclone.authorization.remoteHint")}</p>
              {session.remoteAuthorizeCommand && (
                <pre className="overflow-x-auto whitespace-pre-wrap break-all rounded bg-default-100 p-2 text-xs">
                  {session.remoteAuthorizeCommand}
                </pre>
              )}
              {question.help && (
                <p className="whitespace-pre-wrap text-xs text-default-500">{question.help}</p>
              )}
              {questionForm}
            </RcloneBrowserAuthorization>
          ) : awaiting && question ? (
            <div className="space-y-3">
              <p className="text-sm text-default-600">
                {t([
                  `rclone.authorization.questionHint.${question.name}`,
                  "rclone.authorization.selectionHint",
                ])}
              </p>
              {questionForm}
              {question.help && (
                <Accordion isCompact>
                  <AccordionItem key="help" title={t("rclone.authorization.nativeHelp")}>
                    <p className="whitespace-pre-wrap text-xs text-default-500">{question.help}</p>
                  </AccordionItem>
                </Accordion>
              )}
            </div>
          ) : null}
          {awaiting && (
            <div className="flex gap-2">
              <Button
                size="sm"
                variant="light"
                isDisabled={busy}
                onPress={() =>
                  void perform(async (isCurrent) =>
                    updateSession(
                      check(
                        await api.getRcloneAuthorization(session.id, authorizationRequestOptions),
                      ).data,
                      isCurrent,
                    ),
                  )
                }
              >
                {t("rclone.authorization.refresh")}
              </Button>
              <Button
                size="sm"
                variant="light"
                isDisabled={cancelling}
                isLoading={cancelling}
                onPress={() => void cancelAuthorization()}
              >
                {t("common.action.cancel")}
              </Button>
            </div>
          )}
        </div>
      )}
      {error && (
        <p role="alert" className="text-sm text-danger">
          {problemLabel(error)}
        </p>
      )}
      <Button size="sm" variant="light" isDisabled={busy} onPress={() => void perform(load)}>
        {t("cloudDownload.account.refresh")}
      </Button>
    </div>
  );
}
