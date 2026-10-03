import type { ReactNode } from "react";
import type { ListRowProps } from "react-virtualized";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { useEffect, useMemo, useRef, useState } from "react";
import { useTranslation } from "react-i18next";
import { AutoSizer, CellMeasurer, CellMeasurerCache, List } from "react-virtualized";

const columns = "grid grid-cols-[3rem_minmax(24rem,1fr)_minmax(14rem,1fr)_11rem] gap-4 px-3";

export const TASK_ROW_MIN_HEIGHT = 128;

interface Props {
  tasks: PostParserTask[];
  search: string;
  renderTask: (task: PostParserTask) => ReactNode;
}

const TaskList = ({ tasks, search, renderTask }: Props) => {
  const { t, i18n } = useTranslation();
  const list = useRef<List>(null);
  const [width, setWidth] = useState(0);
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

  const rowRenderer = ({ index, key, parent, style }: ListRowProps) => {
    const task = tasks[index];

    return (
      <CellMeasurer key={key} cache={cache} columnIndex={0} parent={parent} rowIndex={index}>
        {({ registerChild }) => (
          <div
            ref={registerChild}
            aria-rowindex={index + 2}
            className={`${columns} min-h-32 border-b border-default-100 py-4`}
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
    <div className="min-w-0 overflow-x-auto rounded-xl border border-default-200">
      <div
        aria-label={t<string>("postParser.page.title")}
        aria-rowcount={tasks.length + 1}
        className="min-w-[960px]"
        role="table"
      >
        <div
          aria-rowindex={1}
          className={`${columns} bg-default-100/70 py-2.5 text-xs font-medium text-default-500`}
          role="row"
        >
          <span role="columnheader">{t<string>("postParser.table.id")}</span>
          <span role="columnheader">{t<string>("postParser.table.target")}</span>
          <span role="columnheader">{t<string>("postParser.table.results")}</span>
          <span role="columnheader">{t<string>("postParser.table.operations")}</span>
        </div>
        <div className="h-[min(60vh,720px)] min-h-72">
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
