import React, { useEffect } from 'react';
import { Balloon, Progress } from '@alifd/next';
import i18n from 'i18next';
import { SyncMediaLibrary } from '@/sdk/apis';
import { BackgroundTaskStatus } from '@/sdk/constants';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import store from '@/store';
import { useTranslation } from 'react-i18next';
import { Button } from '@/components/bakaui';

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
  const { t } = useTranslation();
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
              {t(taskInfo?.currentProcess)}({taskInfo?.percentage}%)
            </div>
          ) : (
            <Button
              color={'secondary'}
              size={'small'}
              onClick={() => {
                SyncMediaLibrary().invoke();
              }}
            >{t('Sync now')}
            </Button>
          )}
          {(failed || isComplete) && (
            <div className="status">
              {failed && (
                <Balloon
                  trigger={(
                    <div className={'failed'}>
                      {t('Failed')}
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
                    {t('Complete')}
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
