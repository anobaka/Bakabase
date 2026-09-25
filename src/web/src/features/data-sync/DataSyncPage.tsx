import type { Ref } from "react";
import type {
  DiagramFocusRequest,
  DiagramPart,
  DiagramSelectVia,
} from "./components/SyncLinksDiagram";

import { useCallback, useEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { useSearchParams } from "react-router-dom";
import { motion, useReducedMotion } from "framer-motion";
import { AiOutlineLoading3Quarters, AiOutlineReload, AiOutlineSync } from "react-icons/ai";

import { dataSyncApi } from "./api";
import { readDataSyncQuery } from "./routes";
import { useCanManageDefinitionSharing } from "./hooks/useCanManageDefinitionSharing";
import { useDataSyncActions } from "./hooks/useDataSyncActions";
import { useDataSyncPageData } from "./hooks/useDataSyncPageData";
import { useDataSyncWindow } from "./hooks/useDataSyncWindow";
import { overallStatus, waitingElsewhereLine } from "./viewModels";
import AddLinkWizard from "./components/AddLinkWizard";
import {
  buttonClass,
  DataSyncErrorNotice,
  panelClass,
  primaryClass,
  SectionHeading,
  StatusDot,
  toneText,
} from "./components/common";
import EntitySyncList from "./components/EntitySyncList";
import HistoryList from "./components/HistoryList";
import InboxList from "./components/InboxList";
import InvitationDialog from "./components/InvitationDialog";
import LinkDetails from "./components/LinkDetails";
import NotAvailableNotice from "./components/NotAvailableNotice";
import RequestsList from "./components/RequestsList";
import RestorePanel from "./components/RestorePanel";
import ReviewView from "./components/ReviewView";
import SyncLinksDiagram from "./components/SyncLinksDiagram";
import ThisDeviceSection from "./components/ThisDeviceSection";

import ConfirmDialog from "@/features/federation/components/ConfirmDialog";
import { DismissButton } from "@/features/federation/components/common";
import { useDetailsFocus } from "@/features/federation/map/useDetailsFocus";
import { useEscapeKey } from "@/features/federation/map/useEscapeKey";
import { useMediaQuery } from "@/features/federation/map/useMediaQuery";
import HelpCenterButton from "@/components/HelpCenter/HelpCenterButton";
import { DataSyncRequestDirection, RemoteAccessMode } from "@/sdk/constants";

/**
 * The data sync page, under System: every device this one syncs definitions with, drawn first,
 * with the details of the one chosen beside the drawing; the requests; this device's own side.
 *
 * Not `localNodeOnly`: it works in this device's own window, in the desktop app's window
 * showing a server it manages (that server's links, requests and history), and in a browser the
 * server lets in because its mode is Unrestricted — where the controls that would create access
 * are not offered (spec §7.1.5). A browser on a server outside Unrestricted mode is told where to
 * go instead, and nothing is asked of the server.
 */
export default function DataSyncPage() {
  const { t } = useTranslation();
  const reach = useDataSyncWindow();

  if (reach === "asking")
    return (
      <div className="flex items-center gap-2 p-6" data-testid="data-sync-asking" role="status">
        <AiOutlineLoading3Quarters aria-hidden className="animate-spin" />
        {t("dataSync.loading")}
      </div>
    );
  if (reach === "notAllowed") return <NotAvailableNotice />;

  return <DataSync />;
}

/**
 * Below this width the details are shown on demand, in a column beside the drawing that is laid
 * out again for the width that is left — never over it. At 1536 px and wider they are docked.
 * The device map's own breakpoint, so both read alike.
 */
export const DATA_SYNC_ON_DEMAND_QUERY = "(max-width: 1535.98px)";

/** What opened the details: a device's card or its spoke, found again by its id. */
interface Opener {
  nodeId: string;
  part: DiagramPart;
}

function DataSync() {
  const { t } = useTranslation();
  const data = useDataSyncPageData();
  const [params, setParams] = useSearchParams();
  const query = readDataSyncQuery(params);
  const canManageHere = useCanManageDefinitionSharing();
  const onDemand = useMediaQuery(DATA_SYNC_ON_DEMAND_QUERY);
  const reducedMotion = useReducedMotion();
  const { overview, peers } = data;
  const canManage = canManageHere && (overview?.canManageSharing ?? true);

  const [chosen, setChosen] = useState<string>();
  // What the last action said: it outlives the device it was about.
  const [notice, setNotice] = useState<string>();
  // Details shown on demand stay open on what an action said when the device went away.
  const [keptOpen, setKeptOpen] = useState(false);
  const [wizard, setWizard] = useState(false);
  const [invitationFor, setInvitationFor] = useState<{ name?: string }>();
  const [definitions, setDefinitions] = useState<{ open: boolean; onlyApart: boolean }>({
    open: false,
    onlyApart: false,
  });
  const page = useRef<HTMLDivElement>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const details = useRef<HTMLElement>(null);
  const diagramRegion = useRef<HTMLElement>(null);
  const requestsSection = useRef<HTMLElement>(null);
  const definitionsSection = useRef<HTMLElement>(null);
  const restoreSection = useRef<HTMLDivElement>(null);
  const opener = useRef<Opener>();
  // Where the keyboard goes once the details close: to what opened them, in the diagram as it is
  // drawn then — the drawing, or the list it turns into beside the details.
  const [returnTo, setReturnTo] = useState<DiagramFocusRequest>();
  const focusDetails = useRef(false);
  const [, setActionsOver] = useState(0);
  const keyboard = useDetailsFocus({
    page,
    details,
    heading,
    actionEnded: useCallback(() => setActionsOver((count) => count + 1), []),
  });
  const actions = useDataSyncActions(
    data.reload,
    { value: notice, set: setNotice },
    { onStart: keyboard.actionStarted, onEnd: keyboard.actionOver },
  );

  const selected = chosen ? peers.find((peer) => peer.nodeId === chosen) : undefined;
  const detailsOpen = !onDemand || !!selected || keptOpen;

  const select = (nodeId: string, via: DiagramSelectVia, part: DiagramPart) => {
    opener.current = { nodeId, part };
    focusDetails.current = via === "keyboard";
    keyboard.release();
    if (nodeId !== chosen) {
      setNotice(undefined);
      actions.reset();
    }
    setKeptOpen(false);
    setChosen(nodeId);
  };
  const letGo = () => {
    setChosen(undefined);
    setKeptOpen(false);
    setNotice(undefined);
    keyboard.release();
  };
  const close = () => {
    letGo();
    const back = opener.current;

    if (back) setReturnTo((current) => ({ ...back, seq: (current?.seq ?? 0) + 1 }));
  };

  // Links into the page: a link's details, the wizard, the requests.
  const linkId = query.linkId;

  useEffect(() => {
    if (linkId === undefined) return;
    const peer = peers.find((item) => item.linkId === linkId);

    if (peer && chosen !== peer.nodeId) {
      opener.current = { nodeId: peer.nodeId, part: "card" };
      setChosen(peer.nodeId);
    }
    // Only when the link named in the address first shows up.
  }, [linkId, peers.some((item) => item.linkId === linkId)]);

  useEffect(() => {
    if (query.add) setWizard(true);
  }, [query.add]);

  useEffect(() => {
    if (query.tab === "requests" && data.loaded)
      requestsSection.current?.scrollIntoView?.({ block: "start" });
  }, [query.tab, data.loaded]);

  useEffect(() => {
    if (query.restore && data.loaded) restoreSection.current?.scrollIntoView?.({ block: "start" });
  }, [query.restore, data.loaded]);

  // The chosen device went away (its link was reset, its request dismissed): let go, keeping
  // what the action said — details shown on demand stay open to say it.
  useEffect(() => {
    if (!chosen || !data.loaded || selected) return;
    setChosen(undefined);
    setKeptOpen(true);
  }, [chosen, selected, data.loaded]);

  // From the keyboard the details are read out where they appear.
  useEffect(() => {
    if (!chosen || !focusDetails.current) return;
    focusDetails.current = false;
    heading.current?.focus();
  }, [chosen]);

  // Escape in the diagram lets go of the device; in the details, closes them.
  useEscapeKey(diagramRegion, letGo, !!chosen || keptOpen);
  // Enabled once the details are there to listen on: they mount when they open.
  useEscapeKey(details, close, detailsOpen && (onDemand || !!chosen) && !actions.confirmation);

  /**
   * A message's × takes itself away with the message: the keyboard that pressed it goes to the
   * details' heading rather than to the page's body.
   */
  const dismiss = (clear: () => void) => () => {
    const active = document.activeElement;
    const fromMessage = !active || active === document.body || !!details.current?.contains(active);

    clear();
    if (fromMessage) heading.current?.focus();
  };

  /** Closes the first sync review, leaving the link's details open. */
  const closeReview = () => {
    const next = new URLSearchParams(params);

    next.delete("review");
    setParams(next, { replace: true });
  };

  const closeWizard = (nodeId?: string) => {
    setWizard(false);
    if (query.add) {
      const next = new URLSearchParams(params);

      next.delete("add");
      setParams(next, { replace: true });
    }
    if (nodeId) setChosen(nodeId);
  };

  const showDefinitions = () => {
    setDefinitions({ open: true, onlyApart: true });
    // Once the list is open.
    setTimeout(() =>
      definitionsSection.current?.scrollIntoView?.({ block: "start", behavior: "smooth" }),
    );
  };

  if (data.refused) return <NotAvailableNotice />;

  const status = overallStatus(t, overview?.status);
  const elsewhere = waitingElsewhereLine(t, overview?.status);
  const selfName: string = overview?.deviceName || t<string>("dataSync.thisDevice");
  const sharingEnabled = overview?.sharingEnabled ?? false;
  const remoteAccessMode = overview?.remoteAccessMode ?? RemoteAccessMode.Disabled;
  // The review a link into the page names: a link's current one, or an older link's review id.
  const reviewLink =
    query.review && linkId !== undefined
      ? data.links.value?.find((item) => item.id === linkId)
      : undefined;
  const reviewOpen = !!query.reviewId || (query.review && !!reviewLink);
  const outgoingRequestOf = (nodeId: string) =>
    data.requests.value?.find(
      (request) =>
        request.nodeId === nodeId && request.direction === DataSyncRequestDirection.Outgoing,
    )?.requestId;

  return (
    <div
      ref={page}
      className="mx-auto flex max-w-[1500px] flex-col gap-4 p-4 sm:p-6"
      data-testid="data-sync-page"
    >
      <header className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-xs font-medium text-primary">{t("federation.mode")}</p>
          <h1 className="flex items-center gap-2 text-2xl font-semibold">
            <AiOutlineSync aria-hidden />
            {t("dataSync.title")}
            <HelpCenterButton section="dataSync" topic="multiDevice" />
          </h1>
          <p className="mt-2 max-w-3xl text-sm text-default-500">{t("dataSync.description")}</p>
          {status && (
            <p
              className={`mt-1 flex items-start gap-2 text-sm ${toneText[status.tone]}`}
              data-testid="data-sync-overall-status"
            >
              <StatusDot tone={status.tone} />
              <span>
                {status.text}
                {elsewhere ? ` · ${elsewhere}` : ""}
              </span>
            </p>
          )}
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            className={buttonClass}
            disabled={actions.busy}
            type="button"
            onClick={() => void data.reload()}
          >
            <AiOutlineReload aria-hidden />
            {t("dataSync.refresh")}
          </button>
          {peers.some((peer) => peer.linkId !== undefined) && (
            <button
              className={buttonClass}
              data-testid="data-sync-sync-all"
              disabled={actions.busy}
              type="button"
              onClick={() => void actions.run(() => dataSyncApi.syncNow(), ["dataSync"])}
            >
              {t("dataSync.link.syncAll")}
            </button>
          )}
          <button
            className={primaryClass}
            data-testid="data-sync-open-wizard"
            type="button"
            onClick={() => setWizard(true)}
          >
            {t("dataSync.wizard.open")}
          </button>
        </div>
      </header>

      {overview?.status.lastErrorCode === "notAvailable" && (
        <p className="rounded-lg bg-default-100 p-3 text-sm" data-testid="data-sync-not-in-build">
          {t("dataSync.notAvailableYet")}
        </p>
      )}
      {(overview?.restorePending || query.restore) && (
        <div ref={restoreSection}>
          <RestorePanel actions={actions} asked={query.restore} version={data.version} />
        </div>
      )}
      <DataSyncErrorNotice error={data.overviewError} onRetry={() => void data.reload()} />
      {!detailsOpen && (actions.error || notice) && (
        <div className="space-y-2">
          <DataSyncErrorNotice
            error={actions.error}
            onDismiss={() => actions.setError(undefined)}
          />
          {notice && (
            <div
              className="flex items-start justify-between gap-3 rounded-lg bg-primary/10 p-3 text-sm"
              role="status"
            >
              <p>{notice}</p>
              <DismissButton onClick={() => setNotice(undefined)} />
            </div>
          )}
        </div>
      )}

      <div
        className={
          !detailsOpen
            ? undefined
            : onDemand
              ? "grid grid-cols-[minmax(0,1fr)_clamp(300px,34%,380px)] items-start gap-3"
              : "grid grid-cols-[minmax(0,1fr)_minmax(360px,420px)] items-start gap-4"
        }
        data-details={detailsOpen ? "open" : "closed"}
        data-layout={onDemand ? "on-demand" : "docked"}
        data-testid="data-sync-layout"
      >
        <section
          ref={diagramRegion}
          aria-label={t("dataSync.diagram.label")}
          className="min-w-0 space-y-2 rounded-xl border border-default-200 bg-content1 p-2 sm:p-3"
          data-testid="data-sync-diagram-region"
        >
          {overview || data.loaded ? (
            <SyncLinksDiagram
              focusRequest={returnTo}
              peers={peers}
              selectedId={selected?.nodeId}
              self={{
                name: selfName,
                kinds: overview?.kinds ?? [],
                sharingEnabled,
                headless: overview?.isHeadless ?? false,
              }}
              onAdd={() => setWizard(true)}
              onSelect={select}
            />
          ) : (
            <p className="p-6 text-sm" role="status">
              {t("dataSync.loading")}
            </p>
          )}
          {(data.links.error || data.map.error) && (
            <DataSyncErrorNotice
              error={data.links.error ?? data.map.error}
              onRetry={() => void data.reload()}
            />
          )}
          {data.loaded && peers.length === 0 && (
            <p className="px-2 pb-1 text-sm text-default-500" data-testid="data-sync-empty">
              {t("dataSync.diagram.empty")}
            </p>
          )}
          {!detailsOpen && peers.length > 0 && (
            <p className="px-2 pb-1 text-xs text-default-500" data-testid="data-sync-hint">
              {t("dataSync.diagram.hint")}
            </p>
          )}
        </section>
        {detailsOpen && (
          <motion.aside
            ref={details}
            animate={{ x: 0, opacity: 1 }}
            aria-labelledby="data-sync-details-title"
            className={`${panelClass} sticky top-4 max-h-[calc(100vh-2rem)] min-w-0 overflow-y-auto`}
            data-on-demand={onDemand || undefined}
            data-testid="data-sync-details"
            initial={onDemand && !reducedMotion ? { x: 16, opacity: 0 } : false}
            transition={{ duration: 0.16 }}
          >
            {selected ? (
              <LinkDetails
                key={selected.nodeId}
                closable
                actions={actions}
                canManage={canManage}
                headingRef={heading}
                outgoingRequestId={outgoingRequestOf(selected.nodeId)}
                peer={selected}
                remoteAccessMode={remoteAccessMode}
                selfName={selfName}
                sharingEnabled={sharingEnabled}
                onClose={close}
                onCreateCode={() => setInvitationFor({ name: selected.name })}
                onDismissMessage={dismiss}
                onShowDefinitions={showDefinitions}
              />
            ) : (
              <SelfSummary
                closable={onDemand}
                error={actions.error}
                headingRef={heading}
                notice={notice}
                selfName={selfName}
                onClose={close}
                onDismissError={dismiss(() => actions.setError(undefined))}
                onDismissNotice={dismiss(() => setNotice(undefined))}
              />
            )}
          </motion.aside>
        )}
      </div>

      <InboxList
        focus={query.tab === "inbox"}
        initialPeer={query.peer}
        peers={peers}
        version={data.version}
        onChanged={() => void data.reload()}
      />

      <RequestsList
        ref={requestsSection}
        actions={actions}
        canManage={canManage}
        error={data.requests.error}
        outgoing={(data.map.value?.outgoing ?? []).filter(
          (item) => item.outcome === "rejected" || item.outcome === "expired",
        )}
        remoteAccessMode={remoteAccessMode}
        requests={data.requests.value ?? []}
        onRetry={() => void data.reload()}
      />

      {overview && (
        <HistoryList
          links={data.links.value}
          selfName={selfName}
          version={data.version}
          onChanged={() => void data.reload()}
        />
      )}

      {overview && (
        <ThisDeviceSection
          actions={actions}
          canManage={canManage}
          overview={overview}
          readers={data.readers.value}
          readersError={data.readers.error}
          onCreateCode={() => setInvitationFor({})}
          onRetryReaders={() => void data.reload()}
        />
      )}

      {overview && (
        <section
          ref={definitionsSection}
          aria-labelledby="data-sync-definitions"
          className={`${panelClass} space-y-3`}
          data-testid="data-sync-definitions"
        >
          <SectionHeading id="data-sync-definitions" title={t("dataSync.entity.title")}>
            <button
              aria-expanded={definitions.open}
              className={buttonClass}
              type="button"
              onClick={() => setDefinitions((current) => ({ ...current, open: !current.open }))}
            >
              {t(definitions.open ? "dataSync.entity.hide" : "dataSync.entity.show")}
            </button>
          </SectionHeading>
          <p className="text-xs text-default-500">{t("dataSync.entity.intro")}</p>
          {definitions.open && (
            <EntitySyncList
              actions={actions}
              initialOnlyApart={definitions.onlyApart}
              version={data.version}
            />
          )}
        </section>
      )}

      {actions.confirmation && (
        <ConfirmDialog
          busy={actions.busy}
          description={actions.confirmation.description}
          error={actions.confirmationError}
          title={actions.confirmation.title}
          warning={actions.confirmation.warning}
          onCancel={actions.cancelConfirmation}
          onConfirm={actions.confirmCurrent}
        />
      )}
      {wizard && (
        <AddLinkWizard
          actions={actions}
          canManage={canManage}
          remoteAccessMode={remoteAccessMode}
          selfName={selfName}
          sharingEnabled={sharingEnabled}
          onClose={closeWizard}
        />
      )}
      {reviewOpen && (
        <ReviewView
          peerName={reviewLink?.peerName ?? t<string>("dataSync.otherDevice")}
          peerNodeId={reviewLink?.peerNodeId}
          reviewId={query.reviewId ?? reviewLink?.reviewId ?? undefined}
          selfName={selfName}
          onChanged={() => void data.reload()}
          onClose={closeReview}
        />
      )}
      {invitationFor && (
        <InvitationDialog
          actions={actions}
          forName={invitationFor.name}
          onClose={() => setInvitationFor(undefined)}
        />
      )}
    </div>
  );
}

/** The details with no device chosen: what they are for, and what the last action said. */
function SelfSummary({
  selfName,
  headingRef,
  closable,
  notice,
  error,
  onClose,
  onDismissNotice,
  onDismissError,
}: {
  selfName: string;
  headingRef: Ref<HTMLHeadingElement>;
  closable: boolean;
  notice?: string;
  error?: Error;
  onClose: () => void;
  onDismissNotice: () => void;
  onDismissError: () => void;
}) {
  const { t } = useTranslation();

  return (
    <section
      aria-labelledby="data-sync-details-title"
      className="space-y-3"
      data-testid="data-sync-self-summary"
    >
      <header className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-xs font-medium text-secondary">{t("dataSync.thisDevice")}</p>
          <h2
            ref={headingRef}
            className="break-words text-lg font-semibold outline-none"
            id="data-sync-details-title"
            tabIndex={-1}
          >
            {selfName}
          </h2>
        </div>
        {closable && (
          <button
            aria-label={t("dataSync.close")}
            className="-m-1 rounded p-1 text-default-500 hover:bg-default-100"
            type="button"
            onClick={onClose}
          >
            ×
          </button>
        )}
      </header>
      <DataSyncErrorNotice error={error} onDismiss={onDismissError} />
      {notice && (
        <div
          className="flex items-start justify-between gap-3 rounded-lg bg-primary/10 p-3 text-sm"
          role="status"
        >
          <p>{notice}</p>
          <DismissButton onClick={onDismissNotice} />
        </div>
      )}
      <p className="rounded-lg bg-default-50 p-2 text-xs text-default-500">
        {t("dataSync.diagram.hint")}
      </p>
    </section>
  );
}
