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
  PushpinFilled,
  LayoutOutlined,
  ReloadOutlined,
} from "@ant-design/icons";

import DestinationEditor from "./DestinationEditor";
import MoveConfirmation from "./MoveConfirmation";
import TaskList from "./TaskList";
import { getKnownMoveReasonCode, useMoveReasonText, useMoveText } from "./messages";
import { clampGeometry, groupDestinations, parseMovePayload, reorderDestinations } from "./utils";

import { Button, Checkbox, Chip } from "@/components/bakaui";
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
  onToggleGlobal,
  canToggleGlobal,
}: {
  destination: MoveDestination;
  relativeTo?: string;
  available?: boolean;
  onEdit: () => void;
  onDelete: () => void;
  onSort: (from: string, to: string) => void;
  onToggleGlobal: () => void;
  canToggleGlobal: boolean;
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
        <Button
          draggable
          isIconOnly
          aria-label={text("sort")}
          className="sort-handle"
          size="sm"
          title={text("sort")}
          type="button"
          variant="light"
          onDragStart={(e) => {
            e.stopPropagation();
            e.dataTransfer.setData(DESTINATION_MIME, destination.id);
            e.dataTransfer.effectAllowed = "move";
          }}
        >
          <HolderOutlined />
        </Button>
        <strong title={destination.path}>
          {destination.name ||
            normalizedMovePath(destination.path).split("/").at(-1) ||
            destination.path}
        </strong>
        <Button
          isIconOnly
          aria-label={text("openFolder")}
          size="sm"
          title={text("openFolder")}
          type="button"
          variant="light"
          onPress={() => {
            void BApi.tool
              .openFileOrDirectory({ path: destination.path })
              .catch((e) => setError(String(e)));
          }}
        >
          <FolderOpenOutlined />
        </Button>
        <Button
          isIconOnly
          aria-label={text("copyPath")}
          size="sm"
          title={text("copyPath")}
          type="button"
          variant="light"
          onPress={copy}
        >
          <CopyOutlined />
        </Button>
        <Button
          isIconOnly
          aria-label={text(destination.scope === "global" ? "unpinGlobal" : "pinGlobal")}
          aria-pressed={destination.scope === "global"}
          color={destination.scope === "global" ? "primary" : "default"}
          isDisabled={!canToggleGlobal}
          size="sm"
          title={text(destination.scope === "global" ? "unpinGlobal" : "pinGlobal")}
          variant={destination.scope === "global" ? "flat" : "light"}
          onPress={onToggleGlobal}
        >
          {destination.scope === "global" ? <PushpinFilled /> : <PushpinOutlined />}
        </Button>
        <Button
          isIconOnly
          aria-label={text("edit")}
          size="sm"
          title={text("edit")}
          type="button"
          variant="light"
          onPress={onEdit}
        >
          <EditOutlined />
        </Button>
        <Button
          isIconOnly
          aria-label={text("remove")}
          size="sm"
          title={text("remove")}
          type="button"
          variant="light"
          onPress={onDelete}
        >
          <CloseOutlined />
        </Button>
      </div>
      <code className="move-panel-path" title={destination.path}>
        {relativeTo ? destination.path.slice(relativeTo.length + 1) : destination.path}
      </code>
      <div className="move-panel-badges" title={text("related")}>
        {libraries.length ? (
          libraries.map((name) => (
            <Chip key={name} color="primary" radius="sm" size="sm" variant="flat">
              {name}
            </Chip>
          ))
        ) : (
          <span className="move-panel-muted">{text("noLibrary")}</span>
        )}
      </div>
      {available === false && <p className="move-panel-error">{text("unavailable")}</p>}
      {error && <p className="move-panel-error">{error}</p>}
      <Button
        className="move-panel-move-selected"
        isDisabled={!selection.length || !!draft || available === false || !contextReady}
        size="sm"
        type="button"
        variant="flat"
        onPress={() => void prepareMove(destination)}
      >
        {text("moveSelected")} ({selection.length})
      </Button>
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
  const toggleGlobal = (destination: MoveDestination) => {
    if (s.savingOptions || !s.initialized || (destination.scope === "global" && !tabId)) return;
    void act(() =>
      updateMovePanelOptions((options) => {
        const current = options.destinations.find((d) => d.id === destination.id && !d.isDeleted);

        if (!current) return options;
        const scope = current.scope === "global" ? "tab" : "global";
        const ownerTab = scope === "tab" ? tabId : undefined;

        if (scope === "tab" && !ownerTab) throw new Error(text("scopeHint"));
        const sameScope = options.destinations.filter(
          (d) => !d.isDeleted && d.scope === scope && d.tabId === ownerTab,
        );
        const duplicate = sameScope.find(
          (d) =>
            d.id !== current.id && normalizedMovePath(d.path) === normalizedMovePath(current.path),
        );

        return {
          ...options,
          destinations: options.destinations.map((d) =>
            d.id !== current.id
              ? d
              : duplicate
                ? { ...d, isDeleted: true }
                : {
                    ...d,
                    scope,
                    tabId: ownerTab,
                    order: Math.max(-1, ...sameScope.map((d) => d.order)) + 1,
                  },
          ),
        };
      }),
    );
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
          <Button
            isIconOnly
            aria-label={text(s.mode === "docked" ? "float" : "dock")}
            isDisabled={!s.dockAvailable && s.mode === "floating"}
            size="sm"
            title={text(s.mode === "docked" ? "float" : "dock")}
            type="button"
            variant="light"
            onPress={() => setMovePanelMode(s.mode === "docked" ? "floating" : "docked")}
          >
            <LayoutOutlined />
          </Button>
          <Button
            isIconOnly
            aria-label={text("minimize")}
            size="sm"
            title={text("minimize")}
            type="button"
            variant="light"
            onPress={minimizeMovePanel}
          >
            <MinusOutlined />
          </Button>
          <Button
            isIconOnly
            aria-label={text("close")}
            size="sm"
            title={text("close")}
            type="button"
            variant="light"
            onPress={closeMovePanel}
          >
            <CloseOutlined />
          </Button>
        </div>
      </header>
      <div className="move-panel-toolbar">
        <Button size="sm" type="button" variant="flat" onPress={() => setEditor("new")}>
          <PlusOutlined /> {text("add")}
        </Button>
        <Button
          isIconOnly
          aria-label={text("refresh")}
          size="sm"
          title={text("refresh")}
          type="button"
          variant="light"
          onPress={() => void refreshMovePanel()}
        >
          <ReloadOutlined />
        </Button>
        <Button
          aria-pressed={grouped}
          size="sm"
          type="button"
          variant="flat"
          onPress={() => setGrouped(!grouped)}
        >
          {text(grouped ? "flat" : "grouped")}
        </Button>
      </div>
      <div className="move-panel-auto" title={text("autoHelp")}>
        <Checkbox
          isDisabled={s.savingOptions || !s.initialized}
          isSelected={s.options.autoOverwrite}
          size="sm"
          onValueChange={(autoOverwrite) => {
            void act(() => updateMovePanelOptions((options) => ({ ...options, autoOverwrite })));
          }}
        >
          {text("autoOverwrite")}
        </Checkbox>
      </div>
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
        <Button
          size="sm"
          type="button"
          variant="flat"
          onPress={() =>
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
        </Button>
      )}
      {s.pendingDrafts.map((draft) => (
        <Button
          key={draft.id}
          className="mx-2 my-1 h-auto whitespace-normal py-2"
          color="warning"
          isDisabled={!!s.draft}
          size="sm"
          type="button"
          variant="flat"
          onPress={() => resumePendingMoveDraft(draft.id)}
        >
          {text("unresolved")}: {draft.destination.name || draft.destination.path}
        </Button>
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
                          canToggleGlobal={
                            s.initialized && !s.savingOptions && (d.scope === "tab" || !!tabId)
                          }
                          destination={d}
                          relativeTo={group.prefix}
                          onDelete={() => remove(d.id)}
                          onEdit={() => setEditor(d)}
                          onSort={sort}
                          onToggleGlobal={() => toggleGlobal(d)}
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
          <Button
            data-resource-move-panel
            aria-label={text("expand")}
            className="move-panel-minimized"
            size="sm"
            title={text("expand")}
            type="button"
            variant="flat"
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
            onPress={expandMovePanel}
          >
            <FolderOpenOutlined />
            <span>
              {running
                ? `${text(running.status === "stopping" ? "stopping" : "running")} ${Math.round(running.percentage ?? 0)}%`
                : text("idle")}
              {queued > 0 && ` · ${queued} ${text("queued")}`}
              {attention > 0 && ` · ${attention} !`}
            </span>
          </Button>,
          document.body,
        )}
      <MoveConfirmation />
    </>
  );
}
