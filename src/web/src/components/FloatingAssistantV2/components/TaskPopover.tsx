import React from 'react';
import { useTranslation } from 'react-i18next';
import {
  CaretRightOutlined,
  ClearOutlined,
  PauseOutlined,
  MessageOutlined,
} from '@ant-design/icons';
import { BTaskStatus } from '@/sdk/constants';
import {
  Button,
  Divider,
  Switch,
  Tooltip,
} from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { useBTasksStore, selectTasks } from '@/stores/bTasks';
import { useUiOptionsStore } from '@/stores/options';
import { TaskTable } from '@/components/FloatingAssistant/components/TaskTable';

interface Props {
  onOpenChat: () => void;
}

const TaskPopover: React.FC<Props> = ({ onOpenChat }) => {
  const { t } = useTranslation();
  const bTasks = useBTasksStore(selectTasks);
  const hideResourceCovers = useUiOptionsStore((s) => s.data?.hideResourceCovers ?? false);
  const patchUiOptions = useUiOptionsStore((s) => s.patch);

  const clearableTasks = bTasks.filter(
    (task) =>
      !task.isPersistent &&
      (task.status === BTaskStatus.Completed ||
        task.status === BTaskStatus.Error ||
        task.status === BTaskStatus.Cancelled),
  );

  const runningTasks = bTasks.filter((task) => task.status === BTaskStatus.Running);
  const pausedTasks = bTasks.filter((task) => task.status === BTaskStatus.Paused);

  return (
    <div className="flex flex-col gap-2 p-2 min-w-[300px]" onClick={(e) => e.stopPropagation()}>
      <div className="flex flex-col gap-2">
        <div className="flex flex-col gap-1 max-h-[600px] overflow-auto">
          <TaskTable tasks={bTasks} />
        </div>
        <Divider orientation="horizontal" />
        <div className="flex items-center gap-2 flex-wrap">
          {runningTasks.length > 0 && (
            <Tooltip content={t('floatingAssistant.tip.pauseAllRunningTasks')}>
              <Button
                size="sm"
                variant="ghost"
                color="warning"
                onPress={async () => {
                  for (const task of runningTasks) {
                    await BApi.backgroundTask.pauseBackgroundTask(task.id);
                  }
                }}
              >
                <PauseOutlined className="text-base" />
                {t('common.action.pauseAll')}
              </Button>
            </Tooltip>
          )}
          {pausedTasks.length > 0 && (
            <Tooltip content={t('floatingAssistant.tip.resumeAllPausedTasks')}>
              <Button
                size="sm"
                variant="ghost"
                color="secondary"
                onPress={async () => {
                  for (const task of pausedTasks) {
                    await BApi.backgroundTask.resumeBackgroundTask(task.id);
                  }
                }}
              >
                <CaretRightOutlined className="text-base" />
                {t('common.action.resumeAll')}
              </Button>
            </Tooltip>
          )}
          {clearableTasks.length > 0 && (
            <Tooltip content={t('floatingAssistant.tip.clearInactiveTasks')}>
              <Button
                size="sm"
                variant="ghost"
                onPress={() => BApi.backgroundTask.cleanInactiveBackgroundTasks()}
              >
                <ClearOutlined className="text-base" />
                {t('floatingAssistant.action.clearInactiveTasks')}
              </Button>
            </Tooltip>
          )}
        </div>
      </div>

      <Divider orientation="horizontal" />

      <div className="flex flex-col gap-3">
        <Switch
          size="sm"
          isSelected={hideResourceCovers}
          onValueChange={(v) => patchUiOptions({ hideResourceCovers: v })}
        >
          {t('floatingAssistant.label.hideResourceCovers')}
        </Switch>
      </div>

      <Divider orientation="horizontal" />

      {/* Chat hint */}
      <div
        className="flex items-center gap-2 p-2 rounded-lg cursor-pointer transition-colors hover:bg-default-100"
        onClick={onOpenChat}
      >
        <MessageOutlined className="text-primary text-base" />
        <span className="text-sm">{t('bakaChat.hint.doubleClickToChat')}</span>
      </div>
    </div>
  );
};

export default React.memo(TaskPopover);
