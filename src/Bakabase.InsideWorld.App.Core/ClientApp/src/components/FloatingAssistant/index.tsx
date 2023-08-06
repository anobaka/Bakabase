import React, { CSSProperties, useCallback, useEffect, useRef, useState } from 'react';
import Tick from './Tick';
import './index.scss';
import { Balloon, Button, Dialog, Progress, Overlay } from '@alifd/next';
import CustomIcon from '@/components/CustomIcon';
import i18n from 'i18next';
import store from '@/store';
import { BackgroundTaskStatus } from '@/sdk/constants';
import { ClearInactiveBackgroundTasks, RemoveBackgroundTask, StartResourceNfoGenerationTask, StopBackgroundTask, SyncMediaLibrary } from '@/sdk/apis';
import { usePrevious } from 'react-use';
import { useTranslation } from 'react-i18next';
const { Popup } = Overlay;

const AssistantStatus = {
  Idle: 0,
  Working: 1,
  AllDone: 2,
  Failed: 3,
};

const SyncTaskName = 'MediaLibraryService:Sync';
const NfoGenerationTaskName = 'ResourceService:NfoGeneration';

export default () => {
  const [allDoneCircleDrawn, setAllDoneCircleDrawn] = useState('');
  const [status, setStatus] = useState(AssistantStatus.Working);
  const prevStatus = usePrevious(status);
  const [removingTaskId, setRemovingTaskId] = useState<number | undefined>();
  const [tasksVisible, setTasksVisible] = useState(false);

  const { t } = useTranslation();

  const portalRef = useRef<HTMLDivElement | null>(null);

  const tasks = store.useModelState('backgroundTasks');
  // const tasks = [
  //   {
  //     name: 'name',
  //     currentProgress: 'cpcpcpcp',
  //     percentage: 20,
  //     status: BackgroundTaskStatus.Running,
  //   },
  //   {
  //     name: 'name123',
  //     percentage: 80,
  //     status: BackgroundTaskStatus.Failed,
  //     message: 'asdssa8912n3ejsnadas98dsasb9123njksafdsd98fsjd9fdsfdsfsdfdfasdssa8912n3ejsnadas98dsasb9123njksafdsd98fsjd9fdsfdsfsdfdfasdssa8912n3ejsnadas98dsasb9123njksafdsd98fsjd9fdsfdsfsdfdfasdssa8912n3ejsnadas98dsasb9123njksafdsd98fsjd9fdsfdsfsdfdf',
  //   },
  //   {
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

  const renderTaskStatus = (task) => {
    if (task.status == BackgroundTaskStatus.Running) {
      return (
        <div className="progress">{Math.floor(task.percentage)}%</div>
      );
    } else {
      const icon = task.status == BackgroundTaskStatus.Complete ? 'check-circle' : 'close-circle';
      const color = task.status == BackgroundTaskStatus.Complete ? 'green' : 'red';
      return (
        <CustomIcon
          type={icon}
          style={{ color, cursor: 'pointer' }}
          onClick={() => {
            if (task.message) {
              Dialog.show({
                title: task.name,
                content: (
                  <pre>
                    {task.message}
                  </pre>
                ),
                closeable: true,
              });
            }
          }}
        />
      );
    }
  };

  const renderTaskOpts = (task) => {
    switch (task.status) {
      case BackgroundTaskStatus.Running:
        return (
          <div className={'opt'}>
            <CustomIcon
              type={'close'}
              onClick={() => {
                Dialog.confirm({
                  title: t(`Stopping ${task.name}`),
                  content: t('Are you sure to stop this task?'),
                  onOk: () => new Promise(((resolve, reject) => {
                    StopBackgroundTask({
                      id: task.id,
                    }).invoke((a) => {
                      if (!a.code) {
                        resolve();
                      }
                    });
                  })),
                  closeable: true,
                });
              }}
            />
          </div>
        );
      case BackgroundTaskStatus.Complete:
      case BackgroundTaskStatus.Failed:
        return (
          <div className={'opt'}>
            <CustomIcon
              type={'check'}
              onClick={() => {
                setRemovingTaskId(task.id);
              }}
            />
          </div>
        );
    }
  };

  const renderTasks = () => {
    if (tasks?.length > 0) {
      return tasks?.map((t) => {
        return (
          <div
            key={t.id}
            className={`task ${removingTaskId == t.id ? 'removing' : ''}`}
            onTransitionEnd={(evt) => {
              console.log(t.id, removingTaskId, evt.propertyName, evt.target.style.opacity);
              if (evt.propertyName == 'opacity' && t.id == removingTaskId) {
                RemoveBackgroundTask({
                  id: t.id,
                }).invoke((a) => {

                }).finally(() => {
                  // setRemovingTaskId(undefined);
                });
              }
            }}
          >
            <div className="info">
              <div className="title">
                <div className="name">
                  {t.name}
                </div>
                {t.currentProgress && (
                  <div className="current">
                    {t.currentProgress}
                  </div>
                )}
              </div>
              <div className="status">
                {renderTaskStatus(t)}
              </div>
            </div>
            <div className="opts">
              {renderTaskOpts(t)}
            </div>
          </div>
        );
      });
    }
    return (
      <div className="no-task">
        {t('No background task')}
      </div>
    );
  };

  const syncDisabled = tasks.some((a) => a.name == SyncTaskName && a.status == BackgroundTaskStatus.Running);
  const nfoGenerationDisabled = tasks.some((a) => a.name == NfoGenerationTaskName && a.status == BackgroundTaskStatus.Running);
  const activeTasks = tasks.filter((t) => t.status != BackgroundTaskStatus.Running);

  return (
    <>

      <div
        className={`portal ${Object.keys(AssistantStatus)[status]} floating-assistant ${tasksVisible ? '' : 'hide'}`}
        ref={portalRef}
        onClick={() => {
          setTasksVisible(!tasksVisible);
        }}
      >
        {/* Working */}
        <div className="loader" >
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
        <div className={'failed'}>
          <CustomIcon type={'warning-circle'} size={'large'} />
        </div>
      </div>
      <Overlay
        v2
        align={'br tl'}
        hasMask
        target={() => portalRef.current}
        safeNode={() => portalRef.current}
        visible={tasksVisible}
        onRequestClose={() => { setTasksVisible(false); }}
        offset={[-50, -5]}
      >
        <div
          className={'floating-assistant main'}
        >
          <div className="content">
            <div className="tasks">
              {renderTasks()}
            </div>
            <div className="opts-container">
              <div className="opts">
                <Balloon.Tooltip
                  trigger={(
                    <Button
                      className="opt"
                      type={'secondary'}
                      size={'small'}
                      disabled={syncDisabled}
                      onClick={() => {
                          SyncMediaLibrary().invoke();
                        }}
                    >
                      {t('Sync media libraries')}
                    </Button>
                    )}
                  triggerType={'hover'}
                  align={'t'}
                >
                  {t('{{job}} will be started once an hour automatically, but you can trigger it manually by clicking there.', { job: t('Synchronization of media libraries') })}
                </Balloon.Tooltip>
                <Balloon.Tooltip
                  trigger={(
                    <Button
                      size={'small'}
                      className="opt"
                      type={'normal'}
                      disabled={nfoGenerationDisabled}
                      onClick={() => {
                          StartResourceNfoGenerationTask().invoke();
                        }}
                    >
                      {t('Generate nfo files')}
                    </Button>
                    )}
                  triggerType={'hover'}
                  align={'t'}
                >
                  {t('{{job}} will be started once an hour automatically, but you can trigger it manually by clicking there.', { job: t('Nfo files generation') })}
                </Balloon.Tooltip>
                {activeTasks.length > 0 && (
                <Button
                  size={'small'}
                  type={'normal'}
                  onClick={() => ClearInactiveBackgroundTasks().invoke()}
                >{t('Clear inactive tasks')}
                </Button>
                  )}
              </div>
            </div>
          </div>
        </div>
      </Overlay>
    </>
  );
};
