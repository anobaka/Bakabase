import type { ReactNode } from "react";
import type { BakabaseServiceModelsViewDashboardWorkflowsViewModel } from "@/sdk/Api";

import { useTranslation } from "react-i18next";
import { useNavigate } from "react-router-dom";
import { AiOutlineArrowRight, AiOutlineBranches, AiOutlineCloudDownload } from "react-icons/ai";

import { Button, Card, CardBody, Chip } from "@/components/bakaui";
import { DownloadTaskStatus } from "@/sdk/constants";
import { useDownloadTasksStore } from "@/stores/downloadTasks";

export type DashboardWorkflowActivity = BakabaseServiceModelsViewDashboardWorkflowsViewModel;

type Props = { workflows: DashboardWorkflowActivity };

const activeDownloadStatuses = new Set([
  DownloadTaskStatus.InQueue,
  DownloadTaskStatus.Starting,
  DownloadTaskStatus.Downloading,
  DownloadTaskStatus.Stopping,
]);

function ActivityCount({
  label,
  value,
  color = "default",
}: {
  label: string;
  value: number | undefined;
  color?: "default" | "primary" | "warning" | "danger";
}) {
  const { i18n } = useTranslation();

  return (
    <div className="flex min-w-0 flex-col items-start gap-1.5">
      <dt className="text-xs leading-4 text-default-500">{label}</dt>
      <dd className="m-0">
        <Chip
          classNames={{ content: "font-semibold tabular-nums" }}
          color={value ? color : "default"}
          size="sm"
          variant="flat"
        >
          {value == null ? "—" : value.toLocaleString(i18n.language)}
        </Chip>
      </dd>
    </div>
  );
}

function ActivityPanel({
  title,
  description,
  icon,
  action,
  onManage,
  children,
}: {
  title: string;
  description: string;
  icon: ReactNode;
  action: string;
  onManage: () => void;
  children: ReactNode;
}) {
  return (
    <Card className="min-w-0 border border-default-200/70 bg-content1" shadow="none">
      <CardBody className="gap-3 p-4">
        <div className="flex items-center justify-between gap-2">
          <h3 className="m-0 flex min-w-0 items-center gap-2 text-sm font-semibold">
            <span aria-hidden className="flex text-lg text-default-500">
              {icon}
            </span>
            {title}
          </h3>
          <Button
            className="h-7 shrink-0 gap-1 px-2 text-xs"
            endContent={<AiOutlineArrowRight aria-hidden className="text-sm" />}
            size="sm"
            variant="light"
            onPress={onManage}
          >
            {action}
          </Button>
        </div>
        <p className="m-0 text-xs leading-5 text-default-500">{description}</p>
        {children}
      </CardBody>
    </Card>
  );
}

/** Workflow totals come from the page's overview request; downloader totals reuse its live store. */
export default function ActivityOverview({ workflows }: Props) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const initialized = useDownloadTasksStore((state) => state.initialized);
  const activeDownloads = useDownloadTasksStore(
    (state) => state.tasks.filter((task) => activeDownloadStatuses.has(task.status)).length,
  );
  const failedDownloads = useDownloadTasksStore(
    (state) => state.tasks.filter((task) => task.status === DownloadTaskStatus.Failed).length,
  );

  return (
    <section
      aria-label={t<string>("dashboard.activity.title")}
      className="grid grid-cols-1 gap-3 md:grid-cols-2"
    >
      <ActivityPanel
        action={t<string>("dashboard.activity.workflows.action")}
        description={t<string>("dashboard.activity.workflows.description")}
        icon={<AiOutlineBranches />}
        title={t<string>("dashboard.activity.workflows.title")}
        onManage={() => navigate("/workflows")}
      >
        <dl className="m-0 grid grid-cols-3 gap-3">
          <ActivityCount
            color="primary"
            label={t<string>("dashboard.activity.workflows.running")}
            value={workflows.runningCount}
          />
          <ActivityCount
            color="warning"
            label={t<string>("dashboard.activity.workflows.waiting")}
            value={workflows.waitingCount}
          />
          <ActivityCount
            color="danger"
            label={t<string>("dashboard.activity.workflows.failedRecently")}
            value={workflows.failedRecentlyCount}
          />
        </dl>
      </ActivityPanel>
      <ActivityPanel
        action={t<string>("dashboard.activity.downloader.action")}
        description={t<string>("dashboard.activity.downloader.description")}
        icon={<AiOutlineCloudDownload />}
        title={t<string>("dashboard.activity.downloader.title")}
        onManage={() => navigate("/downloader")}
      >
        <div className="flex flex-wrap items-end justify-between gap-3">
          <dl className="m-0 grid flex-1 grid-cols-2 gap-3">
            <ActivityCount
              color="primary"
              label={t<string>("dashboard.activity.downloader.active")}
              value={initialized ? activeDownloads : undefined}
            />
            <ActivityCount
              color="danger"
              label={t<string>("dashboard.activity.downloader.failed")}
              value={initialized ? failedDownloads : undefined}
            />
          </dl>
          {!initialized && (
            <span className="text-xs leading-5 text-default-500" role="status">
              {t<string>("dashboard.activity.downloader.awaitingSync")}
            </span>
          )}
        </div>
      </ActivityPanel>
    </section>
  );
}
