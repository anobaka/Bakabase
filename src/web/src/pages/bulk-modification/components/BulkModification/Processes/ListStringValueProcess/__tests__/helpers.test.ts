import { describe, it, expect } from 'vitest';
import { validate } from '../helpers';
import {
  BulkModificationListStringProcessOperation,
  BulkModificationProcessorOptionsItemsFilterBy,
  BulkModificationStringProcessOperation
} from '@/sdk/constants';
import type { BulkModificationProcessValue } from '@/pages/bulk-modification/components/BulkModification/models';

// Helper to create mock values
const createMockStringListValue = (): string[] => ['item1', 'item2', 'item3'];

const createMockProcessValue = (): BulkModificationProcessValue => ({
  type: 1, // ManuallyInput
  value: 'test',
});

describe('ListStringValueProcess validate', () => {
  describe('Delete operation', () => {
    it('should return undefined (no error) for Delete operation', () => {
      const result = validate(BulkModificationListStringProcessOperation.Delete, undefined);
      expect(result).toBeUndefined();
    });

    it('should return undefined even with options', () => {
      const result = validate(BulkModificationListStringProcessOperation.Delete, { value: createMockStringListValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('SetWithFixedValue operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationListStringProcessOperation.SetWithFixedValue, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when value is missing', () => {
      const result = validate(BulkModificationListStringProcessOperation.SetWithFixedValue, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationListStringProcessOperation.SetWithFixedValue, { value: createMockStringListValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('Append operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationListStringProcessOperation.Append, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when value is missing', () => {
      const result = validate(BulkModificationListStringProcessOperation.Append, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationListStringProcessOperation.Append, { value: createMockStringListValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('Prepend operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationListStringProcessOperation.Prepend, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when value is missing', () => {
      const result = validate(BulkModificationListStringProcessOperation.Prepend, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationListStringProcessOperation.Prepend, { value: createMockStringListValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('Modify operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationListStringProcessOperation.Modify, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when modifyOptions is undefined', () => {
      const result = validate(BulkModificationListStringProcessOperation.Modify, {});
      expect(result).toBe('bulkModification.validation.modifyOptionsRequired');
    });

    it('should return error when filterBy is not All and filterValue is missing', () => {
      const result = validate(BulkModificationListStringProcessOperation.Modify, {
        modifyOptions: {
          filterBy: BulkModificationProcessorOptionsItemsFilterBy.Containing,
          operation: BulkModificationStringProcessOperation.AddToEnd,
        }
      });
      expect(result).toBe('bulkModification.validation.filterValueRequired');
    });

    it('should return error when filterBy is not All and filterValue is empty', () => {
      const result = validate(BulkModificationListStringProcessOperation.Modify, {
        modifyOptions: {
          filterBy: BulkModificationProcessorOptionsItemsFilterBy.Containing,
          filterValue: '',
          operation: BulkModificationStringProcessOperation.AddToEnd,
        }
      });
      expect(result).toBe('bulkModification.validation.filterValueRequired');
    });

    it('should return error when operation is undefined', () => {
      const result = validate(BulkModificationListStringProcessOperation.Modify, {
        modifyOptions: {
          filterBy: BulkModificationProcessorOptionsItemsFilterBy.All,
        }
      });
      expect(result).toBe('bulkModification.validation.operationRequired');
    });

    it('should pass validation to StringValueProcess when modifyOptions are complete', () => {
      // Using AddToEnd which requires a value
      const result = validate(BulkModificationListStringProcessOperation.Modify, {
        modifyOptions: {
          filterBy: BulkModificationProcessorOptionsItemsFilterBy.All,
          operation: BulkModificationStringProcessOperation.AddToEnd,
          options: {} // Missing value, should trigger string validation error
        }
      });
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when all modifyOptions are valid', () => {
      const result = validate(BulkModificationListStringProcessOperation.Modify, {
        modifyOptions: {
          filterBy: BulkModificationProcessorOptionsItemsFilterBy.All,
          operation: BulkModificationStringProcessOperation.AddToEnd,
          options: { value: createMockProcessValue() }
        }
      });
      expect(result).toBeUndefined();
    });

    it('should return undefined with Delete string operation (no value needed)', () => {
      const result = validate(BulkModificationListStringProcessOperation.Modify, {
        modifyOptions: {
          filterBy: BulkModificationProcessorOptionsItemsFilterBy.All,
          operation: BulkModificationStringProcessOperation.Delete,
        }
      });
      expect(result).toBeUndefined();
    });

    it('should validate correctly with Containing filterBy and valid filterValue', () => {
      const result = validate(BulkModificationListStringProcessOperation.Modify, {
        modifyOptions: {
          filterBy: BulkModificationProcessorOptionsItemsFilterBy.Containing,
          filterValue: 'test',
          operation: BulkModificationStringProcessOperation.Delete,
        }
      });
      expect(result).toBeUndefined();
    });

    it('should validate correctly with Matching filterBy and valid filterValue', () => {
      const result = validate(BulkModificationListStringProcessOperation.Modify, {
        modifyOptions: {
          filterBy: BulkModificationProcessorOptionsItemsFilterBy.Matching,
          filterValue: '\\d+',
          operation: BulkModificationStringProcessOperation.Delete,
        }
      });
      expect(result).toBeUndefined();
    });
  });
});
