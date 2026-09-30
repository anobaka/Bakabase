import type { BakabaseAbstractionsModelsDomainConstantsPathMarkAdditionalItem } from "@/sdk/Api";
import type { DragEvent } from "react";
import type { MoveDestination } from "./types";

import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { Rnd } from "react-rnd";
import {
  CloseOutlined,
  CopyOutlined,
  EditOutlined,
  FolderOpenOutlined,
  HolderOutlined,
  MinusOutlined,
  PlusOutlined,
  PushpinOutlined,
  ReloadOutlined,
} from "@ant-design/icons";

import DestinationEditor from "./DestinationEditor";
import MoveConfirmation from "./MoveConfirmation";
import TaskList from "./TaskList";
import { getKnownMoveReasonCode, useMoveReasonText, useMoveText } from "./messages";
import { clampGeometry, groupDestinations, parseMovePayload, reorderDestinations } from "./utils";

import BApi from "@/sdk/BApi";
import { BTaskType, IwFsType, PathMarkAdditionalItem, PathMarkType } from "@/sdk/constants";
import { useBTasksStore } from "@/stores/bTasks";
import { usePathMarksStore } from "@/stores/pathMarks";
import {
  RESOURCE_MOVE_MIME,
  closeMovePanel,
  currentMovePayload,
  expandMovePanel,
  minimizeMovePanel,
  normalizedMovePath,
  prepareMove,
  refreshMovePanel,
  resumePendingMoveDraft,
  setMovePanelDockWidth,
  setMovePanelGeometry,
  setMovePanelMode,
  updateMovePanelOptions,
  useResourceMovePanelStore,
  visibleMoveDestinations,
} from "@/stores/resourceMovePanel";

import "./style.css";

const DESTINATION_MIME = "application/x-bakabase-move-destination";

function useViewport() {
  const [size, setSize] = useState({ width: window.innerWidth, height: window.innerHeight });

  useEffect(() => {
    const resize = () => setSize({ width: window.innerWidth, height: window.innerHeight });

    window.addEventListener("resize", resize);

    return () => window.removeEventListener("resize", resize);
  }, []);

  return size;
}
function usePanelSync() {
  const fingerprint = useBTasksStore((s) =>
    s.tasks
      .filter((t) => t.type === BTaskType.MoveResources)
      .map((t) => `${t.id}:${t.status}`)
      .join("|"),
  );

  useEffect(() => {
    void refreshMovePanel();
  }, [fingerprint]);
  useEffect(() => {
    // Domain snapshots also recover waiting/recovery locks that no longer own a BTask slot.
    const timer = window.setInterval(() => {
      if (document.visibilityState !== "hidden") void refreshMovePanel();
    }, 3000);
    const refresh = () => {
      if (document.visibilityState !== "hidden") void refreshMovePanel();
    };

    window.addEventListener("online", refresh);
    document.addEventListener("visibilitychange", refresh);

    return () => {
      window.clearInterval(timer);
      window.removeEventListener("online", refresh);
      document.removeEventListener("visibilitychange", refresh);
    };
  }, []);
}
function DestinationRow({
  destination,
  relativeTo,
  available,
  onEdit,
  onDelete,
  onSort,
}: {
  destination: MoveDestination;
  relativeTo?: string;
  available?: boolean;
  onEdit: () => void;
  onDelete: () => void;
  onSort: (from: string, to: string) => void;
}) {
  const text = useMoveText();
  const reasonText = useMoveReasonText();
  const [hover, setHover] = useState(false);
  const [error, setError] = useState<string>();
  const selection = useResourceMovePanelStore(
    (s) => s.explicitPayload?.resources ?? s.context.selectedResources,
  );
  const draft = useResourceMovePanelStore((s) => s.draft);
  const contextReady = useResourceMovePanelStore(
    (s) => !!s.sourceContext && !s.sourceContextInvalidated,
  );
  const marks = usePathMarksStore((s) => s.marks);
  const related = [...marks.values()].filter(
    (m) =>
      !m.isDeleted &&
      m.type === PathMarkType.MediaLibrary &&
      (normalizedMovePath(destination.path) === normalizedMovePath(m.path) ||
        normalizedMovePath(destination.path).startsWith(`${normalizedMovePath(m.path)}/`)),
  );
  const libraries = [...new Set(related.map((m) => m.mediaLibrary?.name || `#${m.id}`))];
  const canDrop = (e: DragEvent) =>
    e.dataTransfer.types.includes(RESOURCE_MOVE_MIME) ||
    e.dataTransfer.types.includes(DESTINATION_MIME);
  const drop = (e: DragEvent) => {
    if (!canDrop(e)) return;
    e.preventDefault();
    e.stopPropagation();
    setHover(false);
    const from = e.dataTransfer.getData(DESTINATION_MIME);

    if (from) {
      onSort(from, destination.id);

      return;
    }
    if (available === false || draft || !contextReady) return;
    const payload = parseMovePayload(e.dataTransfer.getData(RESOURCE_MOVE_MIME));

    if (payload) void prepareMove(destination, payload);
    else setError(reasonText("sourceContextRequired"));
  };
  const copy = () => {
    void navigator.clipboard.writeText(destination.path).catch(() => setError(text("copyFailed")));
  };

  return (
    <article
      className={`move-panel-destination ${hover ? "drop-hover" : ""} ${available === false ? "unavailable" : ""}`}
      onDragLeave={(e) => {
        if (!e.currentTarget.contains(e.relatedTarget as Node)) setHover(false);
      }}
      onDragOver={(e) => {
        if (canDrop(e)) {
          e.preventDefault();
          e.dataTransfer.dropEffect = available === false || draft ? "none" : "move";
          setHover(true);
        }
      }}
      onDrop={drop}
    >
      <div className="move-panel-destination-heading">
        <button
          draggable
          aria-label={text("sort")}
          className="icon sort-handle"
          title={text("sort")}
          type="button"
          onDragStart={(e) => {
            e.stopPropagation();
            e.dataTransfer.setData(DESTINATION_MIME, destination.id);
            e.dataTransfer.effectAllowed = "move";
          }}
        >
          <HolderOutlined />
        </button>
        <strong title={destination.path}>
          {destination.name ||
            normalizedMovePath(destination.path).split("/").at(-1) ||
            destination.path}
        </strong>
        <button
          aria-label={text("openFolder")}
          className="icon"
          title={text("openFolder")}
          type="button"
          onClick={() => {
            void BApi.tool
              .openFileOrDirectory({ path: destination.path })
              .catch((e) => setError(String(e)));
          }}
        >
          <FolderOpenOutlined />
        </button>
        <button
          aria-label={text("copyPath")}
          className="icon"
          title={text("copyPath")}
          type="button"
          onClick={copy}
        >
          <CopyOutlined />
        </button>
        <button
          aria-label={text("edit")}
          className="icon"
          title={text("edit")}
          type="button"
          onClick={onEdit}
        >
          <EditOutlined />
        </button>
        <button
          aria-label={text("remove")}
          className="icon"
          title={text("remove")}
          type="button"
          onClick={onDelete}
        >
          <CloseOutlined />
        </button>
      </div>
      <code className="move-panel-path" title={destination.path}>
        {relativeTo ? destination.path.slice(relativeTo.length + 1) : destination.path}
      </code>
      <div className="move-panel-badges" title={text("related")}>
        {libraries.length ? (
          libraries.map((name) => (
            <span key={name} className="move-panel-badge">
              {name}
            </span>
          ))
        ) : (
          <span className="move-panel-muted">{text("noLibrary")}</span>
        )}
      </div>
      {available === false && <p className="move-panel-error">{text("unavailable")}</p>}
      {error && <p className="move-panel-error">{error}</p>}
      <button
        className="move-panel-move-selected"
        disabled={!selection.length || !!draft || available === false || !contextReady}
        type="button"
        onClick={() => void prepareMove(destination)}
      >
        {text("moveSelected")} ({selection.length})
      </button>
    </article>
  );
}
function PanelContent() {
  const text = useMoveText();
  const reasonText = useMoveReasonText();
  const s = useResourceMovePanelStore();
  const tabId = s.explicitPayload ? s.explicitPayload.sourceTabId : s.context.tabId;
  const destinations = useMemo(() => visibleMoveDestinations(s.options, tabId), [s.options, tabId]);
  const [editor, setEditor] = useState<MoveDestination | "new">();
  const [deleted, setDeleted] = useState<string>();
  const [grouped, setGrouped] = useState(false);
  const [availability, setAvailability] = useState<Record<string, boolean>>({});
  const [operationError, setOperationError] = useState<string>();
  const [pathsRatio, setPathsRatio] = useState(53);
  const root = useRef<HTMLDivElement>(null);
  const pathsKey = destinations.map((d) => d.path).join("\n");

  useEffect(() => {
    let alive = true;

    void BApi.pathMark
      .getAllPathMarks(
        {
          additionalItems: (PathMarkAdditionalItem.Property |
            PathMarkAdditionalItem.MediaLibrary) as BakabaseAbstractionsModelsDomainConstantsPathMarkAdditionalItem,
        },
        { showErrorToast: false },
      )
      .then((r) => {
        if (alive && !r.code) usePathMarksStore.getState().setMarks(r.data ?? []);
      })
      .catch(() => {});

    return () => {
      alive = false;
    };
  }, []);
  useEffect(() => {
    let alive = true;
    const check = () =>
      void Promise.all(
        destinations.map(async (d) => {
          try {
            const rsp = await BApi.file.getIwFsEntry({ path: d.path }, { showErrorToast: false });

            return [
              d.id,
              !rsp.code &&
                !!rsp.data &&
                [IwFsType.Directory, IwFsType.Drive].includes(rsp.data.type),
            ] as const;
          } catch {
            return [d.id, false] as const;
          }
        }),
      ).then((entries) => {
        if (alive) setAvailability(Object.fromEntries(entries));
      });

    check();
    const timer = window.setInterval(check, 30000);

    return () => {
      alive = false;
      window.clearInterval(timer);
    };
  }, [pathsKey]);
  const act = async (operation: () => Promise<unknown>) => {
    setOperationError(undefined);
    try {
      await operation();
    } catch (e) {
      setOperationError(e instanceof Error ? e.message : String(e));
    }
  };
  const sort = (from: string, to: string) => {
    void act(() =>
      updateMovePanelOptions((options) => ({
        ...options,
        destinations: reorderDestinations(options.destinations, from, to),
      })),
    );
  };
  const remove = (id: string) => {
    void act(async () => {
      await updateMovePanelOptions((options) => ({
        ...options,
        destinations: options.destinations.map((d) =>
          d.id === id ? { ...d, isDeleted: true } : d,
        ),
      }));
      setDeleted(id);
    });
  };
  const beginResize = (e: React.PointerEvent<HTMLDivElement>) => {
    e.currentTarget.setPointerCapture(e.pointerId);
    const onMove = (event: PointerEvent) => {
      const bounds = root.current?.getBoundingClientRect();

      if (bounds)
        setPathsRatio(
          Math.max(25, Math.min(75, ((event.clientY - bounds.top) / bounds.height) * 100)),
        );
    };
    const onUp = () => {
      window.removeEventListener("pointermove", onMove);
      window.removeEventListener("pointerup", onUp);
    };

    window.addEventListener("pointermove", onMove);
    window.addEventListener("pointerup", onUp, { once: true });
  };

  return (
    <div data-resource-move-panel aria-label={text("title")} className="move-panel" role="region">
      <header className="move-panel-titlebar">
        <h2>{text("title")}</h2>
        <div className="move-panel-title-actions">
          <button
            aria-label={text(s.mode === "docked" ? "float" : "dock")}
            className="icon"
            disabled={!s.dockAvailable && s.mode === "floating"}
            title={text(s.mode === "docked" ? "float" : "dock")}
            type="button"
            onClick={() => setMovePanelMode(s.mode === "docked" ? "floating" : "docked")}
          >
            <PushpinOutlined />
          </button>
          <button
            aria-label={text("minimize")}
            className="icon"
            title={text("minimize")}
            type="button"
            onClick={minimizeMovePanel}
          >
            <MinusOutlined />
          </button>
          <button
            aria-label={text("close")}
            className="icon"
            title={text("close")}
            type="button"
            onClick={closeMovePanel}
          >
            <CloseOutlined />
          </button>
        </div>
      </header>
      <div className="move-panel-toolbar">
        <button type="button" onClick={() => setEditor("new")}>
          <PlusOutlined /> {text("add")}
        </button>
        <button
          aria-label={text("refresh")}
          className="icon"
          title={text("refresh")}
          type="button"
          onClick={() => void refreshMovePanel()}
        >
          <ReloadOutlined />
        </button>
        <button aria-pressed={grouped} type="button" onClick={() => setGrouped(!grouped)}>
          {text(grouped ? "flat" : "grouped")}
        </button>
      </div>
      <label className="move-panel-auto" title={text("autoHelp")}>
        <input
          checked={s.options.autoOverwrite}
          disabled={s.savingOptions || !s.initialized}
          type="checkbox"
          onChange={(e) => {
            const autoOverwrite = e.target.checked;

            void act(() => updateMovePanelOptions((options) => ({ ...options, autoOverwrite })));
          }}
        />
        {text("autoOverwrite")}
      </label>
      {s.options.autoOverwrite && <p className="move-panel-auto-help">{text("autoHelp")}</p>}
      {(s.error || operationError) && (
        <div className="move-panel-error" role="alert">
          {getKnownMoveReasonCode(operationError || s.error)
            ? reasonText(operationError || s.error)
            : operationError || s.error}
        </div>
      )}
      {!s.sourceContext && !s.error && (
        <p className="move-panel-notice">{reasonText("sourceContextRequired")}</p>
      )}
      {deleted && (
        <button
          type="button"
          onClick={() =>
            void act(async () => {
              await updateMovePanelOptions((options) => ({
                ...options,
                destinations: options.destinations.map((d) =>
                  d.id === deleted ? { ...d, isDeleted: false } : d,
                ),
              }));
              setDeleted(undefined);
            })
          }
        >
          {text("undo")}
        </button>
      )}
      {s.pendingDrafts.map((draft) => (
        <button
          key={draft.id}
          className="move-panel-notice"
          disabled={!!s.draft}
          type="button"
          onClick={() => resumePendingMoveDraft(draft.id)}
        >
          {text("unresolved")}: {draft.destination.name || draft.destination.path}
        </button>
      ))}
      <div ref={root} className="move-panel-body">
        <section className="move-panel-path-section" style={{ flexBasis: `${pathsRatio}%` }}>
          <div className="move-panel-section-heading">
            <h3>{text("paths")}</h3>
            <span>
              {currentMovePayload().resources.length} {text("selected")}
            </span>
          </div>
          <div className="move-panel-destinations">
            {!destinations.length && <p className="move-panel-empty">{text("noPaths")}</p>}
            {(["global", "tab"] as const).map((scope) => {
              const scoped = destinations.filter((d) => d.scope === scope);

              return scoped.length ? (
                <div key={scope}>
                  <h4>{text(scope === "global" ? "global" : "local")}</h4>
                  {groupDestinations(scoped, grouped).map((group, i) => (
                    <div key={`${scope}-${i}`} className="move-panel-path-group">
                      {group.prefix && <code className="move-panel-prefix">{group.prefix}</code>}
                      {group.destinations.map((d) => (
                        <DestinationRow
                          key={d.id}
                          available={availability[d.id]}
                          destination={d}
                          relativeTo={group.prefix}
                          onDelete={() => remove(d.id)}
                          onEdit={() => setEditor(d)}
                          onSort={sort}
                        />
                      ))}
                    </div>
                  ))}
                </div>
              ) : null;
            })}
          </div>
        </section>
        <div
          aria-label={text("tasks")}
          aria-orientation="horizontal"
          aria-valuemax={75}
          aria-valuemin={25}
          aria-valuenow={pathsRatio}
          className="move-panel-height-divider"
          role="slider"
          tabIndex={0}
          onKeyDown={(e) => {
            if (e.key === "ArrowUp" || e.key === "ArrowDown") {
              e.preventDefault();
              setPathsRatio((p) =>
                Math.max(25, Math.min(75, p + (e.key === "ArrowDown" ? 5 : -5))),
              );
            }
          }}
          onPointerDown={beginResize}
        />
        <TaskList />
      </div>
      {editor && (
        <DestinationEditor
          destination={editor === "new" ? undefined : editor}
          onClose={() => setEditor(undefined)}
        />
      )}
    </div>
  );
}
export function ResourceMovePanelDock() {
  const s = useResourceMovePanelStore();
  const viewport = useViewport();
  const text = useMoveText();

  useEffect(() => {
    useResourceMovePanelStore.setState({ dockAvailable: true });

    return () => {
      useResourceMovePanelStore.setState({ dockAvailable: false });
    };
  }, []);
  if (!s.open || s.mode !== "docked" || viewport.width < 720) return null;
  const width = Math.min(s.dockWidth, Math.max(300, viewport.width * 0.48));
  const resize = (e: React.PointerEvent<HTMLDivElement>) => {
    e.currentTarget.setPointerCapture(e.pointerId);
    const start = e.clientX;
    const move = (event: PointerEvent) =>
      setMovePanelDockWidth(
        Math.max(300, Math.min(viewport.width * 0.48, width + start - event.clientX)),
      );
    const end = () => {
      window.removeEventListener("pointermove", move);
      window.removeEventListener("pointerup", end);
    };

    window.addEventListener("pointermove", move);
    window.addEventListener("pointerup", end, { once: true });
  };

  return (
    <aside data-resource-move-panel className="move-panel-dock" style={{ width }}>
      <div
        aria-label={text("title")}
        aria-orientation="vertical"
        aria-valuemax={Math.floor(viewport.width * 0.48)}
        aria-valuemin={300}
        aria-valuenow={width}
        className="move-panel-width-divider"
        role="slider"
        tabIndex={0}
        onKeyDown={(e) => {
          if (e.key === "ArrowLeft" || e.key === "ArrowRight") {
            e.preventDefault();
            setMovePanelDockWidth(
              Math.max(
                300,
                Math.min(viewport.width * 0.48, width + (e.key === "ArrowLeft" ? 20 : -20)),
              ),
            );
          }
        }}
        onPointerDown={resize}
      />
      <PanelContent />
    </aside>
  );
}
export default function ResourceMovePanel() {
  usePanelSync();
  const s = useResourceMovePanelStore();
  const viewport = useViewport();
  const text = useMoveText();
  const hoverTimer = useRef<ReturnType<typeof setTimeout>>();

  useEffect(
    () => () => {
      clearTimeout(hoverTimer.current);
    },
    [],
  );
  const geometry = clampGeometry(s.geometry, viewport.width, viewport.height);
  const floating = s.open && (s.mode === "floating" || !s.dockAvailable || viewport.width < 720);
  const running = s.batches.find((b) => b.status === "running" || b.status === "stopping");
  const attention =
    s.pendingDrafts.length +
    s.batches.filter((b) => ["waiting", "failed", "needsRecovery", "partial"].includes(b.status))
      .length;
  const queued = s.batches.filter((b) => b.status === "queued").length;

  return (
    <>
      {floating &&
        createPortal(
          <Rnd
            data-resource-move-panel
            enableResizing
            bounds="window"
            cancel="button,input,select,a"
            className="move-panel-window"
            dragHandleClassName="move-panel-titlebar"
            maxHeight={viewport.height - 16}
            maxWidth={viewport.width - 16}
            minHeight={Math.min(360, viewport.height - 16)}
            minWidth={Math.min(340, viewport.width - 16)}
            position={{ x: geometry.x, y: geometry.y }}
            size={{ width: geometry.width, height: geometry.height }}
            style={{ zIndex: 45, position: "fixed" }}
            onDragStop={(_, d) => setMovePanelGeometry({ ...geometry, x: d.x, y: d.y })}
            onResizeStop={(_, __, ref, ___, position) =>
              setMovePanelGeometry({
                ...position,
                width: ref.offsetWidth,
                height: ref.offsetHeight,
              })
            }
          >
            <PanelContent />
          </Rnd>,
          document.body,
        )}
      {s.minimized &&
        createPortal(
          <button
            data-resource-move-panel
            aria-label={text("expand")}
            className="move-panel-minimized"
            title={text("expand")}
            type="button"
            onClick={expandMovePanel}
            onDragLeave={() => {
              clearTimeout(hoverTimer.current);
              hoverTimer.current = undefined;
            }}
            onDragOver={(e) => {
              if (e.dataTransfer.types.includes(RESOURCE_MOVE_MIME)) {
                e.preventDefault();
                e.dataTransfer.dropEffect = "none";
                if (!hoverTimer.current)
                  hoverTimer.current = setTimeout(() => {
                    hoverTimer.current = undefined;
                    expandMovePanel();
                  }, 500);
              }
            }}
            onDrop={(e) => {
              e.preventDefault();
              clearTimeout(hoverTimer.current);
              hoverTimer.current = undefined;
            }}
          >
            <FolderOpenOutlined />
            <span>
              {running
                ? `${text(running.status === "stopping" ? "stopping" : "running")} ${Math.round(running.percentage ?? 0)}%`
                : text("idle")}
              {queued > 0 && ` · ${queued} ${text("queued")}`}
              {attention > 0 && ` · ${attention} !`}
            </span>
          </button>,
          document.body,
        )}
      <MoveConfirmation />
    </>
  );
}
