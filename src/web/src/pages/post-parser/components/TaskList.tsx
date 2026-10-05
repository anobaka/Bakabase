import type { ReactNode } from "react";
import type { ListRowProps } from "react-virtualized";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AutoSizer, CellMeasurer, CellMeasurerCache, List } from "react-virtualized";

const columns =
  "grid grid-cols-[2.5rem_minmax(15rem,0.85fr)_minmax(22rem,1.65fr)_12.5rem] gap-3 px-3";

export const TASK_ROW_MIN_HEIGHT = 128;

interface Props {
  tasks: PostParserTask[];
  search: string;
  renderTask: (task: PostParserTask) => ReactNode;
  locateRequest?: { taskId: number; sequence: number };
  onLocated?: (sequence: number) => void;
}

const TaskList = ({ tasks, search, renderTask, locateRequest, onLocated }: Props) => {
  const { t, i18n } = useTranslation();
  const list = useRef<List>(null);
  const [width, setWidth] = useState(0);
  const [highlight, setHighlight] = useState<{ taskId: number; sequence: number }>();
  const lastLocation = useRef<number>();
  const cache = useMemo(
    () =>
      new CellMeasurerCache({
        fixedWidth: true,
        defaultHeight: 180,
        minHeight: TASK_ROW_MIN_HEIGHT,
      }),
    [],
  );

  useEffect(() => {
    // Results, line wrapping and translations can all change a row's measured height.
    cache.clearAll();
    list.current?.recomputeRowHeights();
  }, [cache, tasks, width, i18n.language]);

  useEffect(() => {
    list.current?.scrollToPosition(0);
  }, [search]);

  useEffect(() => {
    if (!locateRequest || lastLocation.current === locateRequest.sequence) return;
    const index = tasks.findIndex((task) => task.id === locateRequest.taskId);

    if (index < 0 || !list.current) return;
    // Consume only explicit requests; polling and stage changes must not take over scrolling.
    lastLocation.current = locateRequest.sequence;
    list.current.scrollToRow(index);
    setHighlight(locateRequest);
    onLocated?.(locateRequest.sequence);
  }, [locateRequest, tasks, onLocated]);

  useEffect(() => {
    if (!highlight) return;
    const timer = window.setTimeout(() => setHighlight(undefined), 3000);

    return () => window.clearTimeout(timer);
  }, [highlight]);

  const rowRenderer = ({ index, key, parent, style }: ListRowProps) => {
    const task = tasks[index];

    return (
      <CellMeasurer key={key} cache={cache} columnIndex={0} parent={parent} rowIndex={index}>
        {({ registerChild }) => (
          <div
            ref={registerChild}
            aria-rowindex={index + 2}
            className={`${columns} min-h-32 border-b border-default-100 py-3 ${highlight?.taskId === task.id ? "bg-primary/10 ring-1 ring-inset ring-primary/40" : ""}`}
            data-located={highlight?.taskId === task.id || undefined}
            data-task-id={task.id}
            role="row"
            style={style}
          >
            {renderTask(task)}
          </div>
        )}
      </CellMeasurer>
    );
  };

  return (
    <div className="flex min-h-72 min-w-0 flex-1 flex-col overflow-x-auto rounded-xl border border-default-200">
      <div
        aria-label={t<string>("postParser.page.title")}
        aria-rowcount={tasks.length + 1}
        className="flex min-h-0 min-w-[960px] flex-1 flex-col"
        role="table"
      >
        <div
          aria-rowindex={1}
          className={`${columns} shrink-0 bg-default-100/70 py-2.5 text-xs font-medium text-default-500`}
          role="row"
        >
          <span role="columnheader">{t<string>("postParser.table.id")}</span>
          <span role="columnheader">{t<string>("postParser.table.target")}</span>
          <span role="columnheader">{t<string>("postParser.table.results")}</span>
          <span role="columnheader">{t<string>("postParser.table.operations")}</span>
        </div>
        <div className="min-h-0 flex-1">
          <AutoSizer onResize={({ width: nextWidth }) => setWidth(nextWidth)}>
            {({ width: listWidth, height }) => (
              <List
                ref={list}
                containerRole="presentation"
                deferredMeasurementCache={cache}
                estimatedRowSize={cache.defaultHeight}
                height={height || 360}
                overscanRowCount={3}
                role="rowgroup"
                rowCount={tasks.length}
                rowHeight={cache.rowHeight}
                rowRenderer={rowRenderer}
                scrollToAlignment="center"
                style={{ overflowX: "hidden" }}
                width={listWidth || 960}
              />
            )}
          </AutoSizer>
        </div>
      </div>
    </div>
  );
};

export default TaskList;
