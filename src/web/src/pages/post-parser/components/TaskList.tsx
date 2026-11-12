import type { ReactNode, RefObject } from "react";
import type { PostParserTask } from "@/core/models/PostParserTask";

import { memo, useEffect, useLayoutEffect, useRef, useState } from "react";
import { useTranslation } from "react-i18next";

const columns =
  "grid grid-cols-[2.5rem_minmax(15rem,0.85fr)_minmax(22rem,1.65fr)_12.5rem] gap-3 px-3";

interface Props {
  tasks: PostParserTask[];
  search: string;
  pageKey?: string;
  scrollRef?: RefObject<HTMLDivElement>;
  renderTask: (task: PostParserTask) => ReactNode;
  locateRequest?: { taskId: number; sequence: number };
  onLocated?: (sequence: number) => void;
}

const TaskList = ({
  tasks,
  search,
  pageKey,
  scrollRef,
  renderTask,
  locateRequest,
  onLocated,
}: Props) => {
  const { t } = useTranslation();
  const scroller = useRef<HTMLDivElement>(null);
  const rows = useRef(new Map<number, HTMLDivElement>());
  const [highlight, setHighlight] = useState<{ taskId: number; sequence: number }>();
  const lastLocation = useRef<number>();
  useLayoutEffect(() => {
    const viewport = scrollRef?.current ?? scroller.current;
    if (viewport) viewport.scrollTop = 0;
  }, [search, pageKey, scrollRef]);

  useLayoutEffect(() => {
    if (!locateRequest || lastLocation.current === locateRequest.sequence) return;
    const row = rows.current.get(locateRequest.taskId);
    const viewport = scrollRef?.current ?? scroller.current;
    if (!row || !viewport) return;
    // Stable paged rows let the browser anchor live content changes. Only an
    // explicit navigation request changes the scroll position here.
    lastLocation.current = locateRequest.sequence;
    viewport.scrollTop = Math.max(
      0,
      viewport.scrollTop +
        row.getBoundingClientRect().top -
        viewport.getBoundingClientRect().top -
        Math.max(0, (viewport.clientHeight - row.getBoundingClientRect().height) / 2),
    );
    setHighlight(locateRequest);
    onLocated?.(locateRequest.sequence);
  }, [locateRequest, tasks, onLocated, scrollRef]);

  useEffect(() => {
    if (!highlight) return;
    const timer = window.setTimeout(() => setHighlight(undefined), 3000);

    return () => window.clearTimeout(timer);
  }, [highlight]);

  return (
    <div
      ref={scroller}
      className={`min-w-0 rounded-xl border border-default-200 ${scrollRef ? "shrink-0" : "min-h-0 flex-1 overflow-auto"}`}
      data-post-parser-scroll={scrollRef ? undefined : true}
      tabIndex={scrollRef ? undefined : 0}
    >
      <div
        aria-label={t<string>("postParser.page.title")}
        aria-rowcount={tasks.length + 1}
        className="flex min-w-[960px] flex-col"
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
        <div className="relative" role="rowgroup">
          {tasks.map((task, index) => (
            <div
              key={task.id}
              ref={(element) => {
                if (element) rows.current.set(task.id, element);
                else rows.current.delete(task.id);
              }}
              aria-rowindex={index + 2}
              className={`${columns} min-h-32 border-b border-default-100 py-3 ${highlight?.taskId === task.id ? "bg-primary/10 ring-1 ring-inset ring-primary/40" : ""}`}
              data-located={highlight?.taskId === task.id || undefined}
              data-task-id={task.id}
              role="row"
            >
              {renderTask(task)}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
};

export default memo(TaskList);
