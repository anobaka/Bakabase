import React, { useEffect, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { CloseCircleOutlined } from '@ant-design/icons';
import diff from 'deep-diff';
import { useUpdate } from 'react-use';
import { ResourceTaskType } from '@/sdk/constants';
import BApi from '@/sdk/BApi';
import type ResourceTask from '@/core/models/ResourceTask';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';
import { Button, Chip, Modal } from '@/components/bakaui';
import store from '@/store';
import { buildLogger } from '@/components/utils';

interface IProps {
  resource: any;
  reload?: () => any;
}

const log = buildLogger('TaskCover');

export default ({ resource, reload }: IProps) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const forceUpdate = useUpdate();

  const taskRef = useRef<ResourceTask>();
  useEffect(() => {
    const unsubscribe = store.subscribe(() => {
      const task = store.getState()
        .resourceTasks
        ?.find((t) => t.id == resource.id);
      if (task || taskRef.current) {
        const differences = diff(task, taskRef.current);
        if (differences) {
          // console.log(differences);
          const prevTask = taskRef.current;
          taskRef.current = task;
          log('TaskChanged', differences, 'current: ', task, 'previous: ', prevTask);
          forceUpdate();

          if (prevTask?.percentage != task?.percentage && task?.percentage == 100) {
            reload?.();
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
    <div
      className={'absolute top-0 left-0 z-20 w-full h-full flex flex-col items-center justify-center gap-3'}
      title={taskRef.current.summary}
    >
      <div className={'absolute top-0 left-0 bg-black opacity-80 w-full h-full'} />
      <div className={'font-bold z-20'}>
        {t(ResourceTaskType[taskRef.current.type])}
      </div>
      {taskRef.current.error ? (
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
              children: <pre>{taskRef.current!.error}</pre>,
            })}
          >{t('Error')}
          </Chip>
          <Button
            size={'sm'}
            isIconOnly
            color={'danger'}
            variant={'light'}
            onClick={() => {
              BApi.resource.clearResourceTask(resource.id);
            }}
          >
            <CloseCircleOutlined className={'text-sm'} />
          </Button>
        </div>
      ) : (
        <div className={'font-bold z-20'}>
          {`${taskRef.current.percentage}%`}
        </div>
      )}
      {taskRef.current.error ? null : (
        <div className={'font-bold z-20'}>
          <Button
            size={'sm'}
            color={'danger'}
            onClick={() => {
              createPortal(Modal, {
                defaultVisible: true,
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
              });
            }}
          >
            {t('Stop')}
          </Button>
        </div>
      )}
    </div>
  );
};
