import React, { useEffect, useRef, useState } from 'react';
import './index.scss';
import { usePrevious } from 'react-use';
import { useTranslation } from 'react-i18next';
import {
  CheckCircleOutlined,
  CheckOutlined,
  CloseCircleOutlined,
  CloseOutlined,
  StopOutlined,
} from '@ant-design/icons';
import store from '@/store';
import { BackgroundTaskStatus } from '@/sdk/constants';
import { Button, Chip, Divider, Modal, Popover } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

const AssistantStatus = {
  Idle: 0,
  Working: 1,
  AllDone: 2,
  Failed: 3,
};

type Task = {
  id: string;
  name: string;
  currentProgress?: string;
  percentage: number;
  status: BackgroundTaskStatus;
  message?: string;
};

const SyncTaskName = 'MediaLibraryService:Sync';
const NfoGenerationTaskName = 'ResourceService:NfoGeneration';

export default () => {
  const [allDoneCircleDrawn, setAllDoneCircleDrawn] = useState('');
  const [status, setStatus] = useState(AssistantStatus.Working);
  const prevStatus = usePrevious(status);
  const [removingTaskId, setRemovingTaskId] = useState<string | undefined>();
  const [tasksVisible, setTasksVisible] = useState(false);
  const { createPortal } = useBakabaseContext();

  const { t } = useTranslation();

  const portalRef = useRef<HTMLDivElement | null>(null);

  const tasks = store.useModelState('backgroundTasks');
  // const tasks: Task[] = [
  //   {
  //     id: 'asdsasdas1',
  //     name: 'name',
  //     currentProgress: 'cpcpcpcp',
  //     percentage: 20,
  //     status: BackgroundTaskStatus.Running,
  //   },
  //   {
  //     id: 'asdsasdas2',
  //     name: 'name123',
  //     percentage: 80,
  //     status: BackgroundTaskStatus.Failed,
  //     message: 'asdssa8912n3ejsnadas98dsasb9123njksafdsd98fsjd9fdsfdsfsdfdfasdssa8912n3ejsnadas98dsasb9123njksafdsd98fsjd9fdsfdsfsdfdfasdssa8912n3ejsnadas98dsasb9123njksafdsd98fsjd9fdsfdsfsdfdfasdssa8912n3ejsnadas98dsasb9123njksafdsd98fsjd9fdsfdsfsdfdf',
  //   },
  //   {
  //     id: 'asdsasdas3',
  //     name: 'name21412412412',
  //     currentProgress: 'cpcpcpcp',
  //     percentage: 100,
  //     status: BackgroundTaskStatus.Complete,
  //   },
  // ];

  // console.log(status);

  useEffect(() => {
    const queryTask = setInterval(() => {
      const tempTasks = store.getState().backgroundTasks;
      let newStatus = AssistantStatus.AllDone;
      if (tempTasks.length > 0) {
        const ongoingTasks = tempTasks.filter((a) => a.status == BackgroundTaskStatus.Running);
        if (ongoingTasks.length > 0) {
          newStatus = AssistantStatus.Working;
        } else {
          const failedTasks = tempTasks.filter((a) => a.status == BackgroundTaskStatus.Failed);
          if (failedTasks.length > 0) {
            newStatus = AssistantStatus.Failed;
          } else {
            const doneTasks = tempTasks.filter((a) => a.status == BackgroundTaskStatus.Complete);
            if (doneTasks.length > 0) {
              newStatus = AssistantStatus.AllDone;
            }
          }
        }
      }

      if (newStatus != prevStatus) {
        setStatus(newStatus);
        if (newStatus == AssistantStatus.AllDone) {
          setTimeout(() => {
            setAllDoneCircleDrawn('drawn');
          }, 300);
        }
      }

      return () => {
        clearInterval(queryTask);
      };
    }, 100);
  }, []);

  const renderTaskStatus = (task: Task) => {
    switch (task.status) {
      case BackgroundTaskStatus.Running:
        return (
          <Chip
            size={'sm'}
            variant={'light'}
          >{Math.floor(task.percentage)}%</Chip>
        );
      case BackgroundTaskStatus.Complete:
        return (
          <Chip
            variant={'light'}
            color={'success'}
            size={'sm'}
          >
            <CheckCircleOutlined className={'text-base'} />
          </Chip>
        );
      case BackgroundTaskStatus.Failed:
        return (
          <Button
            size={'sm'}
            color={'danger'}
            variant={'light'}
            isIconOnly
            onClick={() => {
              if (task.message) {
                createPortal(Modal, {
                  defaultVisible: true,
                  size: 'xl',
                  classNames: {
                    wrapper: 'floating-assistant-modal',
                  },
                  title: t('Error'),
                  children: (
                    <pre>
                      {task.message}
                    </pre>
                  ),
                  footer: {
                    actions: ['cancel'],
                  },
                });
              }
            }}
          >
            <CloseCircleOutlined className={'text-base'} />
          </Button>
        );
    }
  };

  const renderTaskOpts = (task: Task) => {
    switch (task.status) {
      case BackgroundTaskStatus.Running:
        return (
          <Button
            color={'danger'}
            variant={'light'}
            size={'sm'}
            isIconOnly
            onClick={() => {
              createPortal(Modal, {
                defaultVisible: true,
                title: t('Stopping task: {{taskName}}', { taskName: task.name }),
                children: t('Are you sure to stop this task?'),
                onOk: async () => await BApi.backgroundTask.stopBackgroundTask(task.id),
              });
            }}
          >
            <StopOutlined className={'text-base'} />
          </Button>
        );
      case BackgroundTaskStatus.Complete:
      case BackgroundTaskStatus.Failed:
        return (
          <Button
            color={'success'}
            variant={'light'}
            size={'sm'}
            isIconOnly
            onClick={() => {
              setRemovingTaskId(task.id);
            }}
          >
            <CheckOutlined className={'text-base'} />
          </Button>
        );
    }
  };

  const renderTasks = () => {
    if (tasks?.length > 0) {
      return tasks?.map((t) => {
        return (
          <div
            key={t.id}
            className={`flex items-center justify-between gap-16 transition-opacity ${removingTaskId ? 'opacity-0' : ''}`}
            onTransitionEnd={(evt) => {
              if (evt.propertyName == 'opacity' && t.id == removingTaskId) {
                BApi.backgroundTask.removeBackgroundTask(t.id);
              }
            }}
          >
            <div className="flex items-center">
              <Chip variant={'light'}>
                {t.name}
              </Chip>
              {t.currentProgress && (
                <Chip
                  variant={'light'}
                  color={'success'}
                >
                  {t.currentProgress}
                </Chip>
              )}
              {renderTaskStatus(t)}
            </div>
            <div className="flex items-center gap-1">
              {renderTaskOpts(t)}
            </div>
          </div>
        );
      });
    }
    return (
      <div className="h-[80px] flex items-center justify-center">
        {t('No background task')}
      </div>
    );
  };

  const syncDisabled = tasks.some((a) => a.name == SyncTaskName && a.status == BackgroundTaskStatus.Running);
  // const nfoGenerationDisabled = tasks.some((a) => a.name == NfoGenerationTaskName && a.status == BackgroundTaskStatus.Running);
  const activeTasks = tasks.filter((t) => t.status != BackgroundTaskStatus.Running);

  return (
    <>
      <Popover
        onOpenChange={visible => {
          setTasksVisible(visible);
        }}
        trigger={(
          <div
            className={`portal ${Object.keys(AssistantStatus)[status]} floating-assistant ${tasksVisible ? '' : 'hide'}`}
            ref={portalRef}
          >
            {/* Working */}
            <div className="loader">
              <span />
            </div>
            {/* AllDone */}
            <div className="tick">
              <svg
                version="1.1"
                x="0px"
                y="0px"
                viewBox="0 0 37 37"
                enableBackground={'new 0 0 37 37'}
                // style="enable-background:new 0 0 37 37;"
                className={allDoneCircleDrawn}
              >
                <path
                  className="circ path"
                  fill={'none'}
                  stroke={'#08c29e'}
                  strokeWidth={3}
                  strokeLinejoin={'round'}
                  strokeMiterlimit={10}
                  d="M30.5,6.5L30.5,6.5c6.6,6.6,6.6,17.4,0,24l0,0c-6.6,6.6-17.4,6.6-24,0l0,0c-6.6-6.6-6.6-17.4,0-24l0,0C13.1-0.2,23.9-0.2,30.5,6.5z"
                />
                <polyline
                  className="tick path"
                  fill={'none'}
                  stroke={'#08c29e'}
                  strokeWidth={3}
                  strokeLinejoin={'round'}
                  strokeMiterlimit={10}
                  points="11.6,20 15.9,24.2 26.4,13.8"
                />
              </svg>
            </div>
            {/* Failed */}
            <div className={'failed flex items-center justify-center w-[48px] h-[48px] '}>
              <CloseCircleOutlined className={'text-5xl'} />
            </div>
          </div>
        )}
      >
        <div className={'flex flex-col gap-2 p-2 min-w-[300px]'}>
          <div className={'font-bold'}>{t('Task list')}</div>
          <Divider orientation={'horizontal'} />
          <div className="flex flex-col gap-1">
            {renderTasks()}
          </div>
          <Divider orientation={'horizontal'} />
          <div className="flex items-center gap-2">
            <Button
              size={'sm'}
              variant={'ghost'}
              disabled={syncDisabled}
              onClick={() => {
                BApi.mediaLibrary.startSyncMediaLibrary();
              }}
            >
              {t('Sync media libraries')}
            </Button>
            {/* <Balloon.Tooltip */}
            {/*   trigger={( */}
            {/*     <Button */}
            {/*       size={'small'} */}
            {/*       className="opt" */}
            {/*       type={'normal'} */}
            {/*       disabled={nfoGenerationDisabled} */}
            {/*       onClick={() => { */}
            {/*             StartResourceNfoGenerationTask().invoke(); */}
            {/*           }} */}
            {/*     > */}
            {/*       {t('Generate nfo files')} */}
            {/*     </Button> */}
            {/*       )} */}
            {/*   triggerType={'hover'} */}
            {/*   align={'t'} */}
            {/* > */}
            {/*   {t('{{job}} will be started once an hour automatically, but you can trigger it manually by clicking there.', { job: t('Nfo files generation') })} */}
            {/* </Balloon.Tooltip> */}
            {activeTasks.length > 0 && (
              <Button
                size={'sm'}
                variant={'ghost'}
                onClick={() => BApi.backgroundTask.clearInactiveBackgroundTasks()}
              >{t('Clear inactive tasks')}
              </Button>
            )}
          </div>
        </div>
      </Popover>
    </>
  );
};
