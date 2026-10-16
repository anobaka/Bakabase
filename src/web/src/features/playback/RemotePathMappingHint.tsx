import type { ManagementSource } from "@/features/management/api";
import type { ManagedPathMappingTarget } from "@/features/federation/components/ManagedPathMappingsModal";

import { useEffect, useState, useSyncExternalStore } from "react";
import { useTranslation } from "react-i18next";
import { TbFolderSymlink, TbX } from "react-icons/tb";

import ManagedPathMappingsModal, {
  loadManagedPathMappingTarget,
} from "@/features/federation/components/ManagedPathMappingsModal";
import { mappedDirectoryPreview } from "@/features/federation/pathMappings";
import { useBakabaseContext } from "@/components/ContextProvider/BakabaseContextProvider";
import { useResourceOwner } from "@/features/management/resourceContext";
import { ClientMode } from "@/sdk/constants";
import { useRemoteAccessStore } from "@/stores/remoteAccess";

// Dismissal is a preference of this controlling device and source, never of the resource owner.
const dismissalEvent = "bakabase:playback-mapping-hint-dismissed";
const dismissedThisSession = new Set<string>();
const dismissed = (key: string) => {
  if (dismissedThisSession.has(key)) return true;
  try {
    return localStorage.getItem(key) === "1";
  } catch {
    return false;
  }
};
const subscribe = (listener: () => void) => {
  window.addEventListener(dismissalEvent, listener);
  window.addEventListener("storage", listener);

  return () => {
    window.removeEventListener(dismissalEvent, listener);
    window.removeEventListener("storage", listener);
  };
};

type Snapshot = Awaited<ReturnType<typeof loadManagedPathMappingTarget>>;
// Only deduplicate concurrent reads. Every newly opened player reads current mappings;
// neither an old result nor a failed read survives in a long-lived cache.
const pending = new Map<string, Promise<Snapshot>>();
const invalidatedEvents = new WeakMap<Event, Set<string>>();
const load = (key: string, target: ManagedPathMappingTarget) => {
  const existing = pending.get(key);

  if (existing) return existing;
  const request = loadManagedPathMappingTarget(target, AbortSignal.timeout(10_000)).finally(() => {
    if (pending.get(key) === request) pending.delete(key);
  });

  pending.set(key, request);

  return request;
};
const isFilePath = (path?: string): path is string =>
  !!path && /^(?:\/|[a-z]:[/\\]|\\\\)/i.test(path);

/** A quiet, optional affordance inside an already-open player, never on every resource card. */
export default function RemotePathMappingHint({
  source: suppliedSource,
  filePath,
  active = true,
}: {
  source?: ManagementSource;
  filePath?: string;
  active?: boolean;
  compact?: boolean;
}) {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const owner = useResourceOwner();
  const source = suppliedSource ?? owner;
  const context = useRemoteAccessStore((state) => state);
  const [snapshot, setSnapshot] = useState<{ key: string; value: Snapshot }>();
  let target: ManagedPathMappingTarget | undefined;

  if (context.context === "known") {
    if (context.clientMode === ClientMode.AllInOne && source && !source.isLocal)
      target = { kind: "manager", serverId: source.serverId };
    else if (
      context.clientMode === ClientMode.PureClient &&
      context.clientHost === "console" &&
      context.serverId &&
      (!source || source.isLocal)
    )
      target = { kind: "relay", serverId: context.serverId };
    // A relay showing B's managed C is not this controller's A -> C connection.
    // Plain browsers likewise have no native path access to the computer running them.
  }
  const controller =
    context.clientMode === ClientMode.PureClient ? context.ownDeviceId : context.serverId;
  const key =
    target && controller
      ? `bakabase:playback:mapping-hint:v1:${encodeURIComponent(controller)}:${target.kind}:${encodeURIComponent(target.serverId)}`
      : "";
  const hidden = useSyncExternalStore(
    subscribe,
    () => !!key && dismissed(key),
    () => false,
  );
  const eligible = active && !!target && !!key && !hidden && isFilePath(filePath);
  const kind = target?.kind;
  const serverId = target?.serverId;

  useEffect(() => {
    if (!eligible || !kind || !serverId) return;
    let current = true;
    let revision = 0;
    const refresh = () => {
      const captured = ++revision;

      // Until a fresh answer arrives, do not suggest a mapping based on stale state.
      setSnapshot(undefined);
      void load(key, { kind, serverId }).then(
        (value) => {
          if (current && captured === revision) setSnapshot({ key, value });
        },
        () => {},
      );
    };
    const saved = (event: Event) => {
      const detail = (event as CustomEvent).detail;

      if (detail?.serverId === serverId && detail?.kind === kind) {
        const invalidated = invalidatedEvents.get(event) ?? new Set<string>();

        if (!invalidated.has(key)) {
          pending.delete(key);
          invalidated.add(key);
          invalidatedEvents.set(event, invalidated);
        }
        refresh();
      }
    };

    refresh();
    window.addEventListener("focus", refresh);
    window.addEventListener("bakabase:path-mappings-saved", saved);

    return () => {
      current = false;
      window.removeEventListener("focus", refresh);
      window.removeEventListener("bakabase:path-mappings-saved", saved);
    };
  }, [eligible, key, kind, serverId]);

  if (
    !eligible ||
    !target ||
    snapshot?.key !== key ||
    !snapshot.value ||
    mappedDirectoryPreview(filePath!, snapshot.value.pathMappings) !== undefined
  )
    return null;

  return (
    <div className="flex min-w-0 items-start gap-1.5 text-xs text-default-500">
      <TbFolderSymlink aria-hidden className="mt-0.5 shrink-0" size={14} />
      <span className="min-w-0 flex-1">
        {t("resource.pathMappingHint.description")}{" "}
        <button
          className="text-primary hover:underline focus-visible:outline focus-visible:outline-2"
          type="button"
          onClick={() =>
            createPortal(ManagedPathMappingsModal, {
              target: target!,
            })
          }
        >
          {t("resource.pathMappingHint.configure")}
        </button>
      </span>
      <button
        aria-label={t("resource.pathMappingHint.dismiss")}
        className="shrink-0 rounded p-0.5 hover:bg-default-100 focus-visible:outline focus-visible:outline-2"
        title={t("resource.pathMappingHint.dismiss")}
        type="button"
        onClick={() => {
          dismissedThisSession.add(key);
          try {
            localStorage.setItem(key, "1");
          } catch {
            // The session preference still prevents repeated prompts when storage is unavailable.
          }
          window.dispatchEvent(new Event(dismissalEvent));
        }}
      >
        <TbX aria-hidden size={14} />
      </button>
    </div>
  );
}
