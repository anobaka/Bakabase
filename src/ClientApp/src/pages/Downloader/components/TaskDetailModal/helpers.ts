import _ from 'lodash';
import type { components } from '@/sdk/BApi2';

type Form = components['schemas']['Bakabase.InsideWorld.Models.RequestModels.DownloadTaskAddInputModel'];
type Task = components['schemas']['Bakabase.InsideWorld.Models.Models.Dtos.DownloadTaskDto'];

export const convertTaskToForm = (task: Task): Form => {
  return {
    ...task,
    keyAndNames: { [task.key]: task.name },
  };
};
