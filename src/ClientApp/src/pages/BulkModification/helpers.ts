import type {
  BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationDto,
  BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationPutRequestModel,
} from '@/sdk/Api';
import type { IBulkModification } from '@/pages/BulkModification/components/BulkModification';

const convertFromApiModel = (d: BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationDto): IBulkModification => {
  return {
    ...d,
    // @ts-ignore
    processes: (d.processes || []).map(p => ({
      ...p,
      value: JSON.parse(p.value!),
    })),
  };
};

const convertToApiModel = (bm: IBulkModification): BakabaseInsideWorldBusinessComponentsBulkModificationAbstractionsModelsDtosBulkModificationPutRequestModel => {
  return {
    ...bm,
    processes: (bm.processes || []).map(p => ({
      ...p,
      value: JSON.stringify(p.value),
    })),
  };
};

export {
  convertFromApiModel,
  convertToApiModel,
};
