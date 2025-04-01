import React, { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { CloseCircleOutlined } from '@ant-design/icons';
import diff from 'deep-diff';
import { useUpdate } from 'react-use';
import _ from 'lodash';
import { BTaskResourceType, BTaskStatus } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { Button, Chip, Modal } from '@/components/bakaui';
import store from '@/store';
import { buildLogger } from '@/components/utils';
import type { BTask } from '@/core/models/BTask';
import { BTaskStopButton } from '@/components/BTask';

interface IProps {
  resource: any;
  reload?: () => any;
}

const log = buildLogger('TaskCover');

enum Action {
  Update = 1,
  Reload = 2,
}

export default ({
                  resource,
                  reload,
                }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const tasksRef = useRef<BTask[]>([]);
  useEffect(() => {
    const unsubscribe = store.subscribe(() => {
      const tasks = store.getState()
        .backgroundTasks
        ?.filter((t) => t.resourceType == BTaskResourceType.Resource && t.resourceKeys?.some(x => x == resource.id)) ?? [];
      if (tasks.length > 0 || tasksRef.current.length > 0) {
        const taskIds = _.uniq(tasks.concat(tasksRef.current).map(t => t.id));
        const actions: Set<Action> = new Set();
        for (const taskId of taskIds) {
          const task = tasks.find(t => t.id == taskId);
          const prevTask = tasksRef.current.find(t => t.id == taskId);
          const differences = diff(prevTask, task);
          if (differences) {
            log('TaskChanged', differences, 'current: ', task, 'previous: ', prevTask);
            actions.add(Action.Update);

            if (prevTask?.percentage != task?.percentage && task?.percentage == 100) {
              actions.add(Action.Reload);
            }
          }
        }
        if (actions.has(Action.Update)) {
          forceUpdate();
        }
        if (actions.has(Action.Reload)) {
          reload?.();
        }
        tasksRef.current = tasks;
      }
    });

    return () => {
      unsubscribe();
    };
  }, []);

  if (tasksRef.current.length == 0) {
    return null;
  }

  const displayingTask = _.sortBy(tasksRef.current, x => {
    switch (x.status) {
      case BTaskStatus.Running:
        return 0;
      case BTaskStatus.Paused:
        return 1;
      case BTaskStatus.Error:
        return -1;
      case BTaskStatus.Completed:
      case BTaskStatus.Stopped:
      case BTaskStatus.NotStarted:
        return 2;
    }
  })[0]!;

  return (
    <div className={'absolute top-0 left-0 z-20 w-full h-full flex flex-col items-center justify-center gap-3'}>
      <div className={'absolute top-0 left-0 bg-black opacity-80 w-full h-full'} />
      <div className={'font-bold z-20'}>
        {displayingTask.name}
      </div>
      {displayingTask.error ? (
        <div className={'font-bold z-20 flex items-center group'}>
          <Chip
            // size={'sm'}
            color={'danger'}
            variant={'light'}
            className={'cursor-pointer'}
            onClick={() => createPortal(Modal, {
              defaultVisible: true,
              title: t('Error'),
              size: 'xl',
              children: <pre>{displayingTask.error}</pre>,
            })}
          >{t('Error')}
          </Chip>
          <Button
            size={'sm'}
            isIconOnly
            color={'danger'}
            variant={'light'}
            onClick={() => {
              BApi.backgroundTask.cleanBackgroundTask(displayingTask.id);
            }}
          >
            <CloseCircleOutlined className={'text-sm'} />
          </Button>
        </div>
      ) : (
        <div className={'font-bold z-20'}>
          {`${displayingTask.percentage}%`}
        </div>
      )}
      {displayingTask.error ? null : (
        <div className={'font-bold z-20'}>
          <BTaskStopButton
            size={'sm'}
            color={'danger'}
            taskId={displayingTask.id}
          />
        </div>
      )}
    </div>
  );
};
