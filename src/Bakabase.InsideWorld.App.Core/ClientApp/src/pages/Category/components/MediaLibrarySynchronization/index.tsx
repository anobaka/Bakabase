import React, { useEffect } from 'react';
import { SyncMediaLibrary } from '@/sdk/apis';
import { Balloon, Button, Progress } from '@alifd/next';
import { BackgroundTaskStatus } from '@/sdk/constants';
import i18n from 'i18next';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import store from '@/store';

const testData = {
  status: BackgroundTaskStatus.Running,
  percentage: 90,
  message: '1232312312312',
  currentProcess: 123212312311,
};

export default ({
  onChange = (status) => {
  },
} = {}) => {
  const backgroundTasks = store.useModelState('backgroundTasks');
  const sortedTasks = backgroundTasks.slice().sort((a, b) => b.startDt.localeCompare(a.startDt));
  const taskInfo = sortedTasks.find((t) => t.name == 'MediaLibraryService:Sync');

  useEffect(() => {
    onChange(taskInfo);
  }, [taskInfo]);

  useEffect(() => {
  }, []);

  const isSyncing = taskInfo?.status == BackgroundTaskStatus.Running;
  const failed = taskInfo?.status == BackgroundTaskStatus.Failed;
  const isComplete = taskInfo?.status == BackgroundTaskStatus.Complete;

  return (
    <div className={'media-library-synchronization'}>
      <div className="main">
        <div className="top">
          {isSyncing ? (
            <div className="process">
              {i18n.t(taskInfo?.currentProcess)}({taskInfo?.percentage}%)
            </div>
          ) : (
            <Button
              type={'secondary'}
              size={'small'}
              onClick={() => {
                SyncMediaLibrary().invoke();
              }}
            >{i18n.t('Sync now')}
            </Button>
          )}
          {(failed || isComplete) && (
            <div className="status">
              {failed && (
                <Balloon
                  trigger={(
                    <div className={'failed'}>
                      {i18n.t('Failed')}
                      &nbsp;
                      <CustomIcon type={'question-circle'} size={'xs'} />
                    </div>
                  )}
                  closable={false}
                  triggerType={'hover'}
                  align={'bl'}
                >
                  <pre>{taskInfo?.message}</pre>
                </Balloon>
              )}
              {
                isComplete && (
                  <div className={'complete'}>
                    <CustomIcon type={'check-circle'} size={'xs'} />
                    &nbsp;
                    {i18n.t('Complete')}
                  </div>
                )
              }
            </div>
          )}
          <div className="opt" />
        </div>
        <div className="bottom">
          <div className="status">
            {isSyncing && (
            <div className={'syncing'}>
              <Progress size="small" backgroundColor={'#d8d8d8'} progressive percent={taskInfo?.percentage} textRender={() => ''} />
            </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
