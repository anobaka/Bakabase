import React, { useEffect } from 'react';
import { Progress } from '@alifd/next';
import { usePrevious } from 'react-use';
import { SyncOutlined } from '@ant-design/icons';
import SynchronizationConfirmModal from '../SynchronizationConfirmModal';
import { BackgroundTaskStatus } from '@/sdk/constants';
import './index.scss';
import CustomIcon from '@/components/CustomIcon';
import store from '@/store';
import { useTranslation } from 'react-i18next';
import { Button, Tooltip } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

const testData = {
  status: BackgroundTaskStatus.Running,
  percentage: 90,
  message: '1232312312312',
  currentProcess: 123212312311,
};

type TaskInfo = {
  status: BackgroundTaskStatus;
  percentage: number;
  message?: string;
  currentProcess?: string;
};

type Props = {
  onComplete?: () => void;
};

export default ({
                  onComplete,
                }: Props) => {
  const { t } = useTranslation();
  const { createPortal } = useBakabaseContext();
  const backgroundTasks = store.useModelState('backgroundTasks');
  const sortedTasks = backgroundTasks.slice().sort((a, b) => b.startDt.localeCompare(a.startDt));
  const taskInfo = sortedTasks.find((t) => t.name == 'MediaLibraryService:Sync');

  const prevTaskInfo = usePrevious(taskInfo);

  useEffect(() => {
    if (taskInfo?.status == BackgroundTaskStatus.Complete && prevTaskInfo?.status != BackgroundTaskStatus.Complete) {
      onComplete && onComplete();
    }
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
                createPortal(
                  SynchronizationConfirmModal, {
                    onOk: async () => await BApi.mediaLibrary.startSyncMediaLibrary(),
                  },
                );
              }}
            >
              <SyncOutlined className={'text-base'} />
              {t('Sync all media libraries')}
            </Button>
          )}
          {(failed || isComplete) && (
            <div className="status">
              {failed && (
                <Tooltip
                  content={(
                    <pre>{taskInfo?.message}</pre>
                  )}
                >
                  <div className={'failed'}>
                    {t('Failed')}
                    &nbsp;
                    <CustomIcon
                      type={'question-circle'}
                      className={'text-base'}
                    />
                  </div>
                </Tooltip>
              )}
              {
                isComplete && (
                  <div className={'complete'}>
                    <CustomIcon
                      type={'check-circle'}
                      className={'text-base'}
                    />
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
                <Progress
                  size="small"
                  backgroundColor={'#d8d8d8'}
                  progressive
                  percent={taskInfo?.percentage}
                  textRender={() => ''}
                />
              </div>
            )}
          </div>
        </div>
      </div>
    </div>
  );
};
