import { Button, Dialog } from '@alifd/next';
import React, { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { diff } from 'deep-diff';
import styles from '@/components/Resource/index.module.scss';
import { ResourceTaskOperationOnComplete, ResourceTaskType } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import type ResourceTask from '@/core/models/ResourceTask';
import store from '@/store';

interface IProps {
  resource: any;
  onRemoved?: (id: number) => any;
}

export default ({ resource, onRemoved }: IProps) => {
  const taskRef = useRef<ResourceTask>();
  const { t } = useTranslation();
  useEffect(() => {
    const unsubscribe = store.subscribe(() => {
      const task = store.getState()
        .resourceTasks
        ?.find((t) => t.id == resource.id);
      if (task || taskRef.current) {
        const differences = diff(task, taskRef.current);
        if (differences) {
          if (task?.percentage == 100 && task.operationOnComplete == ResourceTaskOperationOnComplete.RemoveOnResourceView) {
            onRemoved?.(resource.id);
          } else {
            const prevTask = taskRef.current;
            taskRef.current = task;
            // log('TaskChanged', differences, 'current: ', task, 'previous: ', prevTask);
            // forceUpdate();
          }
        }
      }
    });

    return () => {
      unsubscribe();
    };
  }, []);

  if (!taskRef.current) {
    return null;
  }

  return (
    <div className={styles.taskCover} title={taskRef.current.summary}>
      <div className={styles.type}>
        {ResourceTaskType[taskRef.current.type]}
      </div>
      <div className={styles.percentage}>
        {taskRef.current.error ? (<span
          style={{
            color: 'red',
            cursor: 'pointer',
          }}
          onClick={() => Dialog.show({
            title: t('Error'),
            content: (<pre>{taskRef.current!.error}</pre>),
            closeable: true,
          })}
        >Error
        </span>) : `${taskRef.current.percentage}%`}
      </div>
      <div className={styles.opt}>
        {taskRef.current.error ? (
          <Button
            size={'small'}
            type={'secondary'}
            onClick={() => {
              BApi.resource.clearResourceTask(resource.id);
            }}
          >
            {t('Clear')}
          </Button>
        ) : (
          <Button
            size={'small'}
            warning
            onClick={() => {
              Dialog.confirm({
                title: t('Sure to stop?'),
                onOk: () => {
                  return new Promise(((resolve, reject) => {
                    BApi.backgroundTask.stopBackgroundTask(taskRef.current!.backgroundTaskId)
                      .then((x) => {
                        if (!x.code) {
                          resolve(x);
                        } else {
                          reject();
                        }
                      })
                      .catch(() => {
                        reject();
                      });
                  }));
                },
                closeable: true,
              });
            }}
          >
            {t('Stop')}
          </Button>
        )}

      </div>
    </div>
  );
};
