import type {
  PlaybackKind,
  PlaybackPreference,
} from "@/components/Resource/components/PlayControl/playbackPreferences";

import { useEffect, useState } from "react";
import { useTranslation } from "react-i18next";
import { create } from "zustand";
import { TbSettings } from "react-icons/tb";

import { Button, Modal, Tooltip } from "@/components/bakaui";
import BApi from "@/sdk/BApi";
import { useUserSideActionsRunHere } from "@/stores/remoteAccess";
import { isDesktopAppWindow } from "@/utils/clientSurface";

export type OpeningChoice = { mode: "browser" | "native" | "ask"; playerId?: string | null };
export type OpeningRule = {
  target: string;
  resourceId?: number | null;
  choices: Partial<Record<PlaybackKind, OpeningChoice>>;
};
type Opener = {
  id: string;
  name: string;
  installed: boolean;
  supportsHttp: boolean;
  supportedKinds?: string[] | null;
};
type Snapshot = { rules: OpeningRule[]; openers: Opener[] };
const kinds: PlaybackKind[] = ["image", "audio", "video", "text", "other"];
const storageKey = "bakabase.playback.overrides.v1";
const empty: Snapshot = { rules: [], openers: [] };
const pending = new Map<string, Promise<void>>();
const loadRevisions = new Map<string, number>();
const saveTails = new Map<string, Promise<void>>();
const supportsKind = (player: Opener, kind: PlaybackKind) =>
  player.supportedKinds?.includes(kind) ?? (kind === "audio" || kind === "video");

export const openingChoice = (
  rules: OpeningRule[],
  resourceId: number | undefined,
  kind: PlaybackKind,
) =>
  rules.find((r) => resourceId != null && r.resourceId === resourceId)?.choices[kind] ??
  rules.find((r) => r.resourceId == null)?.choices[kind];

export const normalizeOpeningRules = (value: unknown): OpeningRule[] => {
  if (!Array.isArray(value) || value.length > 1024) return [];

  return value.filter(
    (r): r is OpeningRule =>
      r &&
      typeof r.target === "string" &&
      /^(server:[\w-]{1,128}|node:[\w-]{1,128}:[\w-]{1,128})$/.test(r.target) &&
      (r.resourceId == null ||
        (Number.isInteger(r.resourceId) && r.resourceId > 0 && r.resourceId <= 2147483647)) &&
      r.choices &&
      typeof r.choices === "object" &&
      !Array.isArray(r.choices) &&
      Object.entries(r.choices).every(
        ([kind, choice]: [string, any]) =>
          kinds.includes(kind as PlaybackKind) &&
          choice &&
          ["browser", "native", "ask"].includes(choice.mode) &&
          (choice.playerId == null ||
            (choice.mode === "native" &&
              typeof choice.playerId === "string" &&
              /^[\w-]{1,64}$/.test(choice.playerId))),
      ),
  );
};
const browserRules = () => {
  try {
    return normalizeOpeningRules(JSON.parse(localStorage.getItem(storageKey) ?? "[]"));
  } catch {
    return [];
  }
};

export const useOpeningRulesStore = create<{
  snapshots: Record<string, Snapshot>;
  errors: Record<string, boolean>;
  load: (key: string, target: string, desktop: boolean, force?: boolean) => Promise<void>;
  save: (key: string, rule: OpeningRule, desktop: boolean) => Promise<void>;
}>((set, get) => ({
  snapshots: {},
  errors: {},
  load: async (key, target, desktop, force = false) => {
    if (!force && get().snapshots[key]) return;
    if (pending.has(key)) return pending.get(key);
    const revision = loadRevisions.get(key) ?? 0;
    const request = (async () => {
      try {
        let snapshot = { ...empty, rules: browserRules().filter((r) => r.target === target) };

        if (desktop) {
          const response = await BApi.player.getDevicePlaybackOverrides(
            { target },
            {
              showErrorToast: false,
              signal: AbortSignal.timeout(10_000),
            },
          );

          if (response.code || !response.data) throw new Error("Opening rules unavailable");
          snapshot = {
            rules: normalizeOpeningRules(response.data.rules).filter(
              (rule) => rule.target === target,
            ),
            openers: (response.data.openers as Opener[]) ?? [],
          };
        }
        if (revision === (loadRevisions.get(key) ?? 0))
          set((state) => ({
            snapshots: { ...state.snapshots, [key]: snapshot },
            errors: { ...state.errors, [key]: false },
          }));
      } catch {
        if (revision === (loadRevisions.get(key) ?? 0))
          set((state) => ({
            errors: { ...state.errors, [key]: true },
          }));
      }
    })();

    pending.set(key, request);
    try {
      await request;
    } finally {
      pending.delete(key);
    }
  },
  save: (key, rule, desktop) => {
    const operation = (saveTails.get(key) ?? Promise.resolve())
      .catch(() => {})
      .then(async () => {
        if (normalizeOpeningRules([rule]).length !== 1) throw new Error("Invalid opening rule");
        if (desktop) {
          const response = await BApi.player.setDevicePlaybackOverrides(rule);

          if (response.code) throw new Error("Opening rules could not be saved");
        } else {
          const rules = browserRules().filter(
            (r) => r.target !== rule.target || (r.resourceId ?? null) !== (rule.resourceId ?? null),
          );

          if (Object.keys(rule.choices).length) rules.push(rule);
          if (rules.length > 1024) throw new Error("Too many opening rules");
          localStorage.setItem(storageKey, JSON.stringify(rules));
        }
        // This write was confirmed. Do not let an older in-flight read replace it,
        // or require another read to make the successful choice visible.
        loadRevisions.set(key, (loadRevisions.get(key) ?? 0) + 1);
        set((state) => {
          const previous = state.snapshots[key] ?? empty;
          const currentRules = desktop
            ? previous.rules
            : browserRules().filter((r) => r.target === rule.target);
          const rules = currentRules.filter(
            (r) => (r.resourceId ?? null) !== (rule.resourceId ?? null),
          );

          if (Object.keys(rule.choices).length) rules.push(rule);

          return {
            snapshots: { ...state.snapshots, [key]: { ...previous, rules } },
            errors: { ...state.errors, [key]: false },
          };
        });
      });

    saveTails.set(key, operation);

    return operation.finally(() => {
      if (saveTails.get(key) === operation) saveTails.delete(key);
    });
  },
}));

export function useTargetPlayback(target: string | undefined, resourceId?: number) {
  const nativeAvailable = useUserSideActionsRunHere();
  const desktop = nativeAvailable && isDesktopAppWindow();
  const key = `${desktop ? "desktop" : "browser"}:${target ?? ""}`;
  const store = useOpeningRulesStore();

  useEffect(() => {
    if (target) void store.load(key, target, desktop);
  }, [target, key, desktop, store.load]);
  const snapshot = target ? (store.snapshots[key] ?? empty) : empty;

  return {
    snapshot,
    desktop,
    nativeAvailable,
    error: !!target && store.errors[key],
    ready: !target || !!store.snapshots[key],
    unavailable: !!target && !!store.errors[key] && !store.snapshots[key],
    choice: (kind: PlaybackKind) => openingChoice(snapshot.rules, resourceId, kind),
    preference: (kind: PlaybackKind, fallback: PlaybackPreference): PlaybackPreference => {
      const choice = openingChoice(snapshot.rules, resourceId, kind);

      return choice ? { mode: choice.mode } : fallback;
    },
    reload: () => (target ? store.load(key, target, desktop, true) : Promise.resolve()),
    save: (rule: OpeningRule) => store.save(key, rule, desktop),
  };
}

/** Rules are edited on the viewing device. Only known player IDs cross this API;
 * the resource owner cannot supply an executable or command to run here. */
export function PlaybackOverridesButton({
  target,
  resourceId,
  label,
  compact = false,
}: {
  target?: string;
  resourceId?: number;
  label?: string;
  compact?: boolean;
}) {
  const { t } = useTranslation();
  const rules = useTargetPlayback(target, resourceId);
  const [visible, setVisible] = useState(false);
  const [scope, setScope] = useState<"device" | "resource">("device");
  const [choices, setChoices] = useState<OpeningRule["choices"]>({});
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState(false);
  const [edited, setEdited] = useState(false);
  const scopedId = scope === "resource" ? resourceId : undefined;

  useEffect(() => {
    if (visible && !edited)
      setChoices({
        ...(rules.snapshot.rules.find((r) => (r.resourceId ?? undefined) === scopedId)?.choices ??
          {}),
      });
  }, [visible, scopedId, rules.snapshot, edited]);
  useEffect(() => {
    setVisible(false);
    setEdited(false);
  }, [target, resourceId]);
  if (!target) return null;
  const save = async (clear = false) => {
    setSaving(true);
    setError(false);
    try {
      await rules.save({ target, resourceId: scopedId, choices: clear ? {} : choices });
      setVisible(false);
    } catch {
      setError(true);
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <Tooltip content={t("resource.openingRules.title")}>
        <Button
          aria-label={t("resource.openingRules.title")}
          isIconOnly={compact}
          size="sm"
          variant="light"
          onPress={() => {
            setVisible(true);
            setEdited(false);
            setError(false);
            void rules.reload();
          }}
        >
          {compact ? <TbSettings aria-hidden /> : t("resource.openingRules.title")}
        </Button>
      </Tooltip>
      {visible && (
        <Modal
          defaultVisible
          footer={false}
          size="lg"
          title={t("resource.openingRules.title")}
          onDestroyed={() => setVisible(false)}
        >
          <div className="flex flex-col gap-3">
            <p className="text-sm text-default-500">
              {t(
                rules.desktop
                  ? "resource.openingRules.deviceHint"
                  : "resource.openingRules.browserHint",
                { target: label ?? "" },
              )}
            </p>
            {resourceId != null && (
              <label className="flex items-center gap-2 text-sm">
                {t("resource.openingRules.scope")}
                <select
                  aria-label={t("resource.openingRules.scope")}
                  className="rounded-md border border-default-200 bg-content1 p-2"
                  disabled={saving}
                  value={scope}
                  onChange={(e) => {
                    setEdited(false);
                    setScope(e.target.value as "device" | "resource");
                  }}
                >
                  <option value="device">{t("resource.openingRules.allResources")}</option>
                  <option value="resource">{t("resource.openingRules.thisResource")}</option>
                </select>
              </label>
            )}
            {rules.error && (
              <div className="text-sm text-danger" role="alert">
                {t("resource.openingRules.loadFailed")}
                <Button size="sm" variant="light" onPress={() => void rules.reload()}>
                  {t("resource.playbackPolicy.retryRules")}
                </Button>
              </div>
            )}
            <div className="flex flex-col gap-2">
              {kinds.map((kind) => (
                <div
                  key={kind}
                  className="grid grid-cols-[4rem_1fr] items-center gap-2 rounded-lg border border-default-200 p-2 sm:grid-cols-[4rem_1fr_1fr]"
                >
                  <span className="text-sm">{t(`resource.playback.kind.${kind}`)}</span>
                  <select
                    aria-label={t("resource.openingRules.methodFor", {
                      kind: t(`resource.playback.kind.${kind}`),
                    })}
                    className="min-w-0 rounded-md border border-default-200 bg-content1 p-2 text-sm"
                    disabled={saving || rules.error || !rules.ready}
                    value={choices[kind]?.mode ?? "inherit"}
                    onChange={(e) => {
                      setEdited(true);
                      setChoices((previous) => {
                        const next = { ...previous };

                        if (e.target.value === "inherit") delete next[kind];
                        else next[kind] = { mode: e.target.value as OpeningChoice["mode"] };

                        return next;
                      });
                    }}
                  >
                    <option value="inherit">{t("resource.openingRules.inherit")}</option>
                    <option value="browser">{t("resource.openingRules.browser")}</option>
                    {(rules.nativeAvailable || choices[kind]?.mode === "native") && (
                      <option disabled={!rules.nativeAvailable} value="native">
                        {t("resource.openingRules.native")}
                      </option>
                    )}
                    <option value="ask">{t("resource.openingRules.ask")}</option>
                  </select>
                  {choices[kind]?.mode === "native" && rules.desktop && (
                    <select
                      aria-label={t("resource.openingRules.playerFor", {
                        kind: t(`resource.playback.kind.${kind}`),
                      })}
                      className="col-start-2 min-w-0 rounded-md border border-default-200 bg-content1 p-2 text-sm sm:col-start-auto"
                      disabled={saving || rules.error}
                      value={choices[kind]?.playerId ?? ""}
                      onChange={(e) => {
                        setEdited(true);
                        setChoices((previous) => ({
                          ...previous,
                          [kind]: { mode: "native", playerId: e.target.value || undefined },
                        }));
                      }}
                    >
                      <option value="">{t("resource.openingRules.profilePlayer")}</option>
                      <option value="system-default">
                        {t("resource.openingRules.systemDefault")}
                      </option>
                      {choices[kind]?.playerId &&
                        choices[kind]?.playerId !== "system-default" &&
                        !rules.snapshot.openers.some(
                          (player) =>
                            player.id === choices[kind]?.playerId && supportsKind(player, kind),
                        ) && (
                          <option disabled value={choices[kind]!.playerId!}>
                            {choices[kind]!.playerId} ·{" "}
                            {t("resource.openingRules.playerUnavailable")}
                          </option>
                        )}
                      {rules.snapshot.openers
                        .filter((player) => supportsKind(player, kind))
                        .map((player) => (
                          <option key={player.id} disabled={!player.installed} value={player.id}>
                            {player.name}
                            {!player.installed
                              ? ` · ${t("resource.openingRules.notInstalled")}`
                              : ""}
                          </option>
                        ))}
                    </select>
                  )}
                </div>
              ))}
            </div>
            <p className="text-xs text-default-500">{t("resource.openingRules.priority")}</p>
            {error && (
              <p className="text-sm text-danger" role="alert">
                {t("resource.openingRules.saveFailed")}
              </p>
            )}
            <div className="flex items-center justify-between gap-2">
              <Button
                isDisabled={saving || rules.error || !rules.ready}
                size="sm"
                variant="light"
                onPress={() => void save(true)}
              >
                {t("resource.openingRules.reset")}
              </Button>
              <Button
                color="primary"
                isDisabled={!rules.ready || rules.error}
                isLoading={saving}
                size="sm"
                onPress={() => void save()}
              >
                {t("common.action.save")}
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </>
  );
}
