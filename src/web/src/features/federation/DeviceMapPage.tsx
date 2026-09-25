import type { DeviceGraph } from "./map/graph";
import type { MapSelection, SelectVia } from "./map/DeviceMapCanvas";
import type { MapSource } from "./map/useDeviceMapData";

import { useCallback, useEffect, useLayoutEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { Link } from "react-router-dom";
import { motion, useReducedMotion } from "framer-motion";
import {
  AiOutlineApartment,
  AiOutlineLoading3Quarters,
  AiOutlineRadarChart,
  AiOutlineReload,
  AiOutlineSync,
} from "react-icons/ai";

import {
  buttonClass,
  ErrorNotice,
  FederationAccess,
  panelClass,
  primaryClass,
} from "./components/common";
import { FederationError } from "./transport";
import { devicesRoute } from "./switching";
import { buildDeviceGraph, followKeys, SELF_ID } from "./map/graph";
import DeviceMapCanvas from "./map/DeviceMapCanvas";
import DeviceMapLegend from "./map/DeviceMapLegend";
import DeviceMapList from "./map/DeviceMapList";
import DeviceMapPanel from "./map/DeviceMapPanel";
import { localPlatform } from "./map/localPlatform";
import { focusMapItem, mapItemOf } from "./map/mapFocus";
import { useDetailsFocus } from "./map/useDetailsFocus";
import { useDeviceMapData } from "./map/useDeviceMapData";
import { useEscapeKey } from "./map/useEscapeKey";
import { useMediaQuery } from "./map/useMediaQuery";

import { useRemoteAccessStore } from "@/stores/remoteAccess";
import { DATA_SYNC_ROUTE } from "@/features/data-sync/routes";

/** Where the device map lives. */
export const DEVICE_MAP_ROUTE = "/federation/map";

/**
 * What the reader chose, and what identifies it beyond its id: an action can turn the record
 * a node was built from into another — a device found nearby into a request, a request into
 * a device — and the selection follows it there.
 */
export interface ChosenSelection {
  selection: MapSelection;
  /** Identity keys of the chosen device, most specific first (see `MapNode.keys`). */
  keys: string[];
  /** Which panel shows it: the same one keeps what it said while it follows the device. */
  panel: number;
}

const nodeOfSelection = (graph: DeviceGraph, selection: MapSelection) => {
  const id =
    selection.type === "node"
      ? selection.id
      : graph.edges.find((edge) => edge.id === selection.id)?.nodeId;

  return id === SELF_ID ? graph.self : graph.nodes.find((node) => node.id === id);
};

/**
 * What is still there to show after a refresh: the same selection; when a relationship went
 * away but its device did not, that device; when the device's record was replaced, the node
 * that now carries one of its identity keys — the same relationship there, if it has one.
 */
export const resolveSelection = (
  graph: DeviceGraph,
  chosen?: Pick<ChosenSelection, "selection" | "keys">,
): MapSelection | undefined => {
  if (!chosen) return undefined;
  const { selection, keys } = chosen;
  const hasNode = (id: string) => id === SELF_ID || graph.nodes.some((node) => node.id === id);

  if (selection.type === "node" && hasNode(selection.id)) return selection;
  if (selection.type === "edge") {
    if (graph.edges.some((edge) => edge.id === selection.id)) return selection;
    const nodeId = selection.id.slice(selection.id.indexOf(":") + 1);

    if (hasNode(nodeId)) return { type: "node", id: nodeId };
  }
  const successor = followKeys(graph, keys);

  if (!successor) return undefined;
  if (selection.type === "edge") {
    const kind = selection.id.slice(0, selection.id.indexOf(":"));
    const edge = graph.edges.find((item) => item.nodeId === successor.id && item.kind === kind);

    if (edge) return { type: "edge", id: edge.id };
  }

  return { type: "node", id: successor.id };
};

/**
 * The multi-device mode as a picture: this device, the devices it knows, and every
 * relationship between them — library sharing and management — each of which can be
 * selected and acted on. Only this device's own window can show it, like the other
 * multi-device pages.
 */
export default function DeviceMapPage() {
  return (
    <FederationAccess>
      <DeviceMap />
    </FederationAccess>
  );
}

/**
 * Below this width the details are shown on demand: with nothing selected the map has the
 * page's whole width; while they are open they stand beside it, and the map is laid out again
 * for the width that is left — never drawn under them.
 */
export const ON_DEMAND_QUERY = "(max-width: 1535.98px)";

/** How long the details wait for the record an action said is coming, e.g. an approved device. */
export const AWAIT_SUCCESSOR_MS = 60_000;
/** How often what it will show up in is re-read meanwhile. */
const AWAIT_POLL_MS = 2000;

/** A record an action said will appear — found by these keys once it does. */
interface Awaited {
  keys: string[];
  sources: MapSource[];
  until: number;
}

function DeviceMap() {
  const { t } = useTranslation();
  const data = useDeviceMapData();
  const serverName = useRemoteAccessStore((state) => state.serverName);
  const platform = useMemo(() => localPlatform(), []);
  const { status, servers, access, dataSync, discovery } = data;
  const graph = useMemo(
    () =>
      buildDeviceGraph({
        status,
        servers,
        access,
        dataSync,
        discovery: { sharing: discovery.sharing, management: discovery.management },
        selfName: serverName,
        selfPlatform: platform,
      }),
    [
      status,
      servers,
      access,
      dataSync,
      discovery.sharing,
      discovery.management,
      serverName,
      platform,
    ],
  );
  const onDemand = useMediaQuery(ON_DEMAND_QUERY);
  const reducedMotion = useReducedMotion();
  const [chosen, setChosen] = useState<ChosenSelection>();
  const selection = resolveSelection(graph, chosen);
  // What the last action said: it outlives the record it was about.
  const [notice, setNotice] = useState<string>();
  // A record an action said is on its way: the details wait for it rather than let go.
  const [awaited, setAwaited] = useState<Awaited>();
  // Details shown on demand stay open on what the last action said when the record they
  // showed went away.
  const [keptOpen, setKeptOpen] = useState(false);
  const panels = useRef(0);
  const page = useRef<HTMLDivElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const panel = useRef<HTMLElement>(null);
  const mapRegion = useRef<HTMLElement>(null);
  // What opened the details, by what it stands for: the control itself can be gone by the time
  // they close — the drawing turned into the list when they opened beside it, and back again.
  const opener = useRef<MapSelection>();
  // Where the keyboard goes once the details have closed.
  const [returnTo, setReturnTo] = useState<MapSelection>();
  const focusPanel = useRef(false);
  const [, setActionsOver] = useState(0);
  const keyboard = useDetailsFocus({
    page,
    details: panel,
    heading,
    actionEnded: useCallback(() => setActionsOver((count) => count + 1), []),
  });
  const settled = !!(
    status ||
    servers ||
    access ||
    dataSync ||
    data.sharingError ||
    data.serversError ||
    data.accessError ||
    data.dataSyncError
  );
  const alone = settled && graph.nodes.length === 0;
  const searched = !discovery.running && discovery.sharing !== undefined;
  const found = graph.nodes.filter((node) => node.ghost).length;
  const selectionKey = selection ? `${selection.type}:${selection.id}` : "";
  const selectedNode = selection ? nodeOfSelection(graph, selection) : undefined;

  const waiting = !!awaited && !followKeys(graph, awaited.keys);
  const detailsOpen = !onDemand || !!chosen || keptOpen;

  /** The reader chose something: a device other than the one shown gets a panel of its own. */
  const choose = (next: MapSelection) => {
    const node = nodeOfSelection(graph, next);
    const same = !!chosen && !!node && node.id === selectedNode?.id;

    if (!same) setNotice(undefined);
    setAwaited(undefined);
    setKeptOpen(false);
    setChosen({
      selection: next,
      keys: node?.keys ?? [],
      panel: same ? chosen!.panel : ++panels.current,
    });
  };
  const select = (next: MapSelection, via: SelectVia) => {
    // What had the keyboard; else — a pointer on a control that takes no focus — what was chosen.
    opener.current = mapItemOf(document.activeElement, mapRegion.current) ?? next;
    focusPanel.current = via === "keyboard";
    keyboard.release();
    choose(next);
  };
  const letGo = () => {
    setChosen(undefined);
    setNotice(undefined);
    setAwaited(undefined);
    setKeptOpen(false);
    keyboard.release();
  };
  const close = () => {
    letGo();
    setReturnTo(opener.current ?? { type: "node", id: SELF_ID });
  };
  /**
   * An action turned the shown record into another: follow it by what it will carry. When
   * that record only appears later — a device approved here joins the listing once it has
   * collected its key — `wait` names where it will appear, re-read until it does.
   */
  const follow = (keys: string[], wait?: MapSource[]) => {
    setChosen((current) =>
      current ? { ...current, keys: [...new Set([...keys, ...current.keys])] } : current,
    );
    if (wait?.length) setAwaited({ keys, sources: wait, until: Date.now() + AWAIT_SUCCESSOR_MS });
  };

  // Escape on the map lets go of the selection (and closes details shown on demand), where the
  // keyboard already is.
  useEscapeKey(mapRegion, letGo, !!selection || keptOpen);

  // A refresh replaced the chosen record: the selection moves to its successor and keeps its
  // panel, or — when nothing stands for it any more, and nothing is on its way — lets go,
  // keeping what the action said (details shown on demand stay open to say it).
  useEffect(() => {
    if (!chosen || !settled) return;
    if (!selection) {
      if (waiting) return;
      setChosen(undefined);
      setAwaited(undefined);
      setKeptOpen(true);

      return;
    }
    if (selection.type === chosen.selection.type && selection.id === chosen.selection.id) return;
    setChosen({
      selection,
      keys: [...new Set([...(selectedNode?.keys ?? []), ...chosen.keys])],
      panel: chosen.panel,
    });
  }, [chosen, selection?.type, selection?.id, selectedNode, settled, waiting]);

  // What an action said is coming has come.
  useEffect(() => {
    if (awaited && !waiting) setAwaited(undefined);
  }, [awaited, waiting]);

  // Waiting for it: re-read where it will appear, for a while.
  useEffect(() => {
    if (!awaited || !waiting) return;
    const poll = setInterval(() => {
      if (!document.hidden) void data.reload(awaited.sources);
    }, AWAIT_POLL_MS);
    const giveUp = setTimeout(() => setAwaited(undefined), Math.max(0, awaited.until - Date.now()));

    return () => {
      clearInterval(poll);
      clearTimeout(giveUp);
    };
  }, [awaited, waiting, data.reload]);

  // The details closed: the keyboard goes back to what opened them, in the map as it is drawn
  // now — the same device or relationship, else its device, else this device. Should the map be
  // laid out again for the width they leave, and a drawing replace the list or a list the
  // drawing, the canvas carries the keyboard over to the same device there.
  useLayoutEffect(() => {
    if (!returnTo) return;
    setReturnTo(undefined);
    focusMapItem(mapRegion.current, returnTo);
  }, [returnTo]);

  // From the keyboard the details are read out where they appear.
  useEffect(() => {
    if (!selectionKey || !focusPanel.current) return;
    focusPanel.current = false;
    heading.current?.focus();
  }, [selectionKey]);

  const sharingUnavailable =
    data.sharingError instanceof FederationError &&
    data.sharingError.code === "SharingStateUnavailable";

  return (
    <div
      ref={page}
      className="mx-auto flex max-w-[1500px] flex-col gap-4 p-4 sm:p-6"
      data-testid="device-map-page"
    >
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-xs font-medium text-primary">{t("federation.mode")}</p>
          <h1 className="flex items-center gap-2 text-2xl font-semibold">
            <AiOutlineApartment aria-hidden />
            {t("federation.map.title")}
          </h1>
          <p className="mt-2 max-w-3xl text-sm text-default-500">{t("federation.map.intro")}</p>
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            className={buttonClass}
            disabled={data.sharingLoading || data.serversLoading}
            type="button"
            onClick={() => void data.refreshAll()}
          >
            <AiOutlineReload aria-hidden />
            {t("federation.refresh")}
          </button>
          <button
            aria-busy={discovery.running || undefined}
            className={buttonClass}
            disabled={discovery.running}
            type="button"
            onClick={() => void data.discover()}
          >
            {discovery.running ? (
              <AiOutlineLoading3Quarters aria-hidden className="animate-spin" />
            ) : (
              <AiOutlineRadarChart aria-hidden />
            )}
            {t(
              discovery.running
                ? "federation.servers.add.discovering"
                : "federation.servers.add.discover",
            )}
          </button>
          <Link className={buttonClass} to={devicesRoute()}>
            {t("federation.devices.title")}
          </Link>
          <Link className={buttonClass} to="/federation">
            {t("federation.title")}
          </Link>
          <Link className={buttonClass} to={DATA_SYNC_ROUTE}>
            <AiOutlineSync aria-hidden />
            {t("dataSync.title")}
          </Link>
        </div>
      </header>

      {(data.sharingError ||
        data.serversError ||
        data.accessError ||
        data.dataSyncError ||
        discovery.error) && (
        <div className="space-y-2" data-testid="device-map-errors">
          {data.sharingError && (
            <div className="space-y-1">
              <p className="text-xs text-default-500">{t("federation.map.source.sharing")}</p>
              <ErrorNotice
                error={data.sharingError}
                onRetry={() => void data.reload(["sharing"])}
              />
              {sharingUnavailable && (
                <Link className="text-xs text-primary underline" to={devicesRoute()}>
                  {t("federation.devices.title")}
                </Link>
              )}
            </div>
          )}
          {data.serversError && (
            <div className="space-y-1">
              <p className="text-xs text-default-500">{t("federation.map.source.servers")}</p>
              <ErrorNotice error={data.serversError} onRetry={() => void data.refreshAll()} />
            </div>
          )}
          {data.accessError && (
            <div className="space-y-1">
              <p className="text-xs text-default-500">{t("federation.map.source.access")}</p>
              <ErrorNotice error={data.accessError} onRetry={() => void data.refreshAll()} />
            </div>
          )}
          {data.dataSyncError && (
            <div className="space-y-1">
              <p className="text-xs text-default-500">{t("federation.map.source.dataSync")}</p>
              <ErrorNotice
                error={data.dataSyncError}
                onRetry={() => void data.reload(["dataSync"])}
              />
            </div>
          )}
          <ErrorNotice error={discovery.error} onRetry={() => void data.discover()} />
        </div>
      )}
      {searched && !discovery.error && (
        <p className={found || alone ? "sr-only" : "text-sm text-default-500"} role="status">
          {found
            ? t("federation.map.discovery.found", { count: found })
            : t("federation.map.discovery.none")}
        </p>
      )}

      <div
        className={
          !detailsOpen
            ? undefined
            : onDemand
              ? // The details take their width from the map, which is laid out again for the
                // rest: nothing of it is ever under them. Narrower in a narrower window, to
                // leave the map what they can.
                "grid grid-cols-[minmax(0,1fr)_clamp(300px,31%,360px)] items-start gap-3"
              : "grid grid-cols-[minmax(0,1fr)_minmax(340px,400px)] items-start gap-4"
        }
        data-details={detailsOpen ? "open" : "closed"}
        data-layout={onDemand ? "on-demand" : "docked"}
        data-testid="device-map-layout"
      >
        <section
          ref={mapRegion}
          aria-label={t("federation.map.title")}
          className="space-y-2 rounded-xl border border-default-200 bg-content1 p-2 sm:p-3"
        >
          {settled ? (
            <DeviceMapCanvas
              footer={
                alone ? (
                  <EmptyState
                    discovering={discovery.running}
                    searched={searched}
                    onDiscover={() => void data.discover()}
                  />
                ) : undefined
              }
              graph={graph}
              selection={selection}
              onSelect={select}
            />
          ) : (
            <p className="p-6 text-sm" role="status">
              {t("federation.loading")}
            </p>
          )}
          <DeviceMapLegend graph={graph} />
          {!detailsOpen && (
            // What the details say with nothing selected, while they are closed.
            <p className="px-2 pb-1 text-xs text-default-500" data-testid="device-map-hint">
              {t("federation.map.hint")}
            </p>
          )}
        </section>
        {detailsOpen && (
          <motion.aside
            ref={panel}
            animate={{ x: 0, opacity: 1 }}
            aria-labelledby="device-map-panel-title"
            className={`${panelClass} sticky top-4 max-h-[calc(100vh-2rem)] min-w-0 overflow-y-auto`}
            data-on-demand={onDemand || undefined}
            data-testid="device-map-details"
            initial={onDemand && !reducedMotion ? { x: 16, opacity: 0 } : false}
            transition={{ duration: 0.16 }}
          >
            <DeviceMapPanel
              // A panel belongs to one device — through whatever records stand for it; with
              // nothing selected it shows this one.
              key={selection && chosen ? `panel-${chosen.panel}` : "overview"}
              access={access}
              closable={onDemand || !!selection}
              dataSync={dataSync}
              graph={graph}
              headingRef={heading}
              notice={{ value: notice, set: setNotice }}
              overview={!selection}
              selection={selection ?? { type: "node", id: SELF_ID }}
              status={status}
              onActionEnd={keyboard.actionOver}
              onActionStart={keyboard.actionStarted}
              onChanged={data.reload}
              onClose={close}
              onFollow={follow}
              onSelect={choose}
            />
          </motion.aside>
        )}
      </div>

      <DeviceMapList graph={graph} />
    </div>
  );
}

/** This device alone: what the mode is, and the way to the first connection. */
function EmptyState({
  discovering,
  searched,
  onDiscover,
}: {
  discovering: boolean;
  searched: boolean;
  onDiscover: () => void;
}) {
  const { t } = useTranslation();

  return (
    <div
      className="mx-auto mb-2 mt-1 max-w-xl space-y-2 rounded-xl border border-dashed border-default-300 bg-content1 p-4 text-center"
      data-testid="device-map-empty"
    >
      <h2 className="font-semibold">{t("federation.map.empty.title")}</h2>
      <p className="text-sm text-default-500">{t("federation.map.empty.body")}</p>
      {searched && !discovering && (
        <p className="text-sm text-warning-600 dark:text-warning">
          {t("federation.map.discovery.none")}
        </p>
      )}
      <div className="flex flex-wrap justify-center gap-2 pt-1">
        <button className={primaryClass} disabled={discovering} type="button" onClick={onDiscover}>
          {discovering && <AiOutlineLoading3Quarters aria-hidden className="animate-spin" />}
          {t(
            discovering ? "federation.servers.add.discovering" : "federation.servers.add.discover",
          )}
        </button>
        <Link className={buttonClass} to={devicesRoute()}>
          {t("federation.devices.title")}
        </Link>
      </div>
    </div>
  );
}
