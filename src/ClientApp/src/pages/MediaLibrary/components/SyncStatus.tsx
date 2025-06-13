import { MdOutlineStopCircle } from 'react-icons/md';
import { GrResume } from 'react-icons/gr';
import { FaRegCircleCheck, FaRegCirclePause } from 'react-icons/fa6';
import { AiOutlineSync, AiOutlineWarning } from 'react-icons/ai';
import { useEffect, useRef } from 'react';
import { Button, Chip, CircularProgress, Modal } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import type { BTask } from '@/core/models/BTask';
import store from '@/store';
import { BTaskStatus } from '@/sdk/constants';
import { useBakabaseContext } from '@/components/ContextProvider/BakabaseContextProvider';

type Props = {
  id: number;
  onSyncCompleted?: () => any;
};

const SyncTaskPrefix = 'SyncMediaLibrary_';
const BuildTaskId = (id: number) => `${SyncTaskPrefix}${id}`;

type ElementType = 'progress' | 'cancel' | 'pause' | 'resume' | 'start' | 'error' | 'completed';

export default ({
                  id,
                  onSyncCompleted,
                }: Props) => {
  const { createPortal } = useBakabaseContext();

  const bTasks = store.useModelState('bTasks');
  const taskId = BuildTaskId(id);
  const task = bTasks?.find(d => d.id == taskId) as BTask;

  const prevStatusRef = useRef(task?.status);

  useEffect(() => {
    if (prevStatusRef.current != undefined &&
      prevStatusRef.current != BTaskStatus.Completed &&
      task?.status == BTaskStatus.Completed) {
      onSyncCompleted?.();
    }
    prevStatusRef.current = task?.status;
  }, [onSyncCompleted, task]);

  // const elementTypes: ElementType[] = ['progress', 'cancel', 'pause', 'resume', 'start', 'error', 'completed'];
  const elementTypes: ElementType[] = [];
  if (task) {
    switch (task.status) {
      case BTaskStatus.NotStarted:
      case BTaskStatus.Running:
        elementTypes.push('progress');
        elementTypes.push('pause');
        elementTypes.push('cancel');
        break;
      case BTaskStatus.Paused:
        elementTypes.push('progress');
        elementTypes.push('start');
        elementTypes.push('cancel');
        break;
      case BTaskStatus.Error:
        elementTypes.push('progress');
        elementTypes.push('error');
        elementTypes.push('start');
        break;
      case BTaskStatus.Completed:
        elementTypes.push('completed');
        elementTypes.push('start');
        break;
      case BTaskStatus.Cancelled:
        elementTypes.push('start');
        break;
    }
  } else {
    elementTypes.push('start');
  }

  const components = elementTypes.map(et => {
    switch (et) {
      case 'progress':
        return (
          <CircularProgress
            showValueLabel
            size="sm"
            value={task.percentage}
          />
        );
      case 'cancel':
        return (
          <Button
            variant={'light'}
            color={'danger'}
            size={'sm'}
            isIconOnly
            onPress={async () => {
              await BApi.backgroundTask.stopBackgroundTask(taskId);
            }}
          >
            <MdOutlineStopCircle className={'text-lg'} />
          </Button>
        );
      case 'pause':
        return (
          <Button
            variant={'light'}
            color={'warning'}
            size={'sm'}
            isIconOnly
            onPress={async () => {
              await BApi.backgroundTask.pauseBackgroundTask(taskId);
            }}
          >
            <FaRegCirclePause className={'text-lg'} />
          </Button>
        );
      case 'resume':
        return (
          <Button
            variant={'light'}
            color={'secondary'}
            size={'sm'}
            isIconOnly
            onPress={async () => {
              await BApi.backgroundTask.resumeBackgroundTask(taskId);
            }}
          >
            <GrResume className={'text-lg'} />
          </Button>
        );
      case 'start':
        return (
          <Button
            variant={'light'}
            color={'secondary'}
            size={'sm'}
            isIconOnly
            onPress={async () => {
              await BApi.mediaLibraryV2.syncMediaLibraryV2(id);
            }}
          >
            <AiOutlineSync className={'text-lg'} />
          </Button>
        );
      case 'error':
        return (
          <Button
            variant={'light'}
            color={'danger'}
            size={'sm'}
            isIconOnly
            onPress={async () => {
              createPortal(Modal, {
                  defaultVisible: true,
                  size: 'xl',
                  children: (
                    <pre>
                      {task.error}
                    </pre>
                  ),
                  footer: {
                    actions: ['cancel'],
                  },
                },
              );
            }}
          >
            <AiOutlineWarning className={'text-lg'} />
          </Button>
        );
      case 'completed':
        return (
          <Chip variant={'light'} color={'success'}>
            <FaRegCircleCheck className={'text-lg'} />
          </Chip>
        );
      default:
        return null;
    }
  });


  return (
    <div className="flex items-center">
      {components}
    </div>
  );
};
