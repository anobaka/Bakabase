import { describe, it, expect } from 'vitest';
import { validate } from '../helpers';
import { BulkModificationStringProcessOperation } from '@/sdk/constants';
import type { StringProcessOptions } from '../models';
import type { BulkModificationProcessValue } from '@/pages/bulk-modification/components/BulkModification/models';

// Helper to create a mock process value
const createMockValue = (): BulkModificationProcessValue => ({
  type: 1, // ManuallyInput
  value: 'test',
});

describe('StringValueProcess validate', () => {
  describe('Delete operation', () => {
    it('should return undefined (no error) for Delete operation', () => {
      const result = validate(BulkModificationStringProcessOperation.Delete, undefined);
      expect(result).toBeUndefined();
    });

    it('should return undefined even with options for Delete operation', () => {
      const result = validate(BulkModificationStringProcessOperation.Delete, { value: createMockValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('SetWithFixedValue operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationStringProcessOperation.SetWithFixedValue, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when value is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.SetWithFixedValue, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationStringProcessOperation.SetWithFixedValue, { value: createMockValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('AddToStart operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationStringProcessOperation.AddToStart, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when value is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.AddToStart, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationStringProcessOperation.AddToStart, { value: createMockValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('AddToEnd operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationStringProcessOperation.AddToEnd, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when value is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.AddToEnd, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationStringProcessOperation.AddToEnd, { value: createMockValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('AddToAnyPosition operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationStringProcessOperation.AddToAnyPosition, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when value is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.AddToAnyPosition, { index: 5 });
      expect(result).toBe('bulkModification.validation.valueAndIndexRequired');
    });

    it('should return error when index is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.AddToAnyPosition, { value: createMockValue() });
      expect(result).toBe('bulkModification.validation.valueAndIndexRequired');
    });

    it('should return error when index is negative', () => {
      const result = validate(BulkModificationStringProcessOperation.AddToAnyPosition, {
        value: createMockValue(),
        index: -1
      });
      expect(result).toBe('bulkModification.validation.valueAndIndexRequired');
    });

    it('should return undefined when both value and valid index are provided', () => {
      const result = validate(BulkModificationStringProcessOperation.AddToAnyPosition, {
        value: createMockValue(),
        index: 0
      });
      expect(result).toBeUndefined();
    });

    it('should return undefined when index is 0', () => {
      const result = validate(BulkModificationStringProcessOperation.AddToAnyPosition, {
        value: createMockValue(),
        index: 0
      });
      expect(result).toBeUndefined();
    });
  });

  describe('RemoveFromStart operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromStart, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when count is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromStart, {});
      expect(result).toBe('bulkModification.validation.countRequired');
    });

    it('should return error when count is negative', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromStart, { count: -1 });
      expect(result).toBe('bulkModification.validation.countRequired');
    });

    it('should return undefined when count is 0', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromStart, { count: 0 });
      expect(result).toBeUndefined();
    });

    it('should return undefined when count is positive', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromStart, { count: 5 });
      expect(result).toBeUndefined();
    });
  });

  describe('RemoveFromEnd operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromEnd, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when count is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromEnd, {});
      expect(result).toBe('bulkModification.validation.countRequired');
    });

    it('should return error when count is negative', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromEnd, { count: -1 });
      expect(result).toBe('bulkModification.validation.countRequired');
    });

    it('should return undefined when count is valid', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromEnd, { count: 3 });
      expect(result).toBeUndefined();
    });
  });

  describe('RemoveFromAnyPosition operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromAnyPosition, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when count is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromAnyPosition, { index: 0 });
      expect(result).toBe('bulkModification.validation.countAndIndexRequired');
    });

    it('should return error when index is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromAnyPosition, { count: 5 });
      expect(result).toBe('bulkModification.validation.countAndIndexRequired');
    });

    it('should return error when count is negative', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromAnyPosition, { count: -1, index: 0 });
      expect(result).toBe('bulkModification.validation.countAndIndexRequired');
    });

    it('should return error when index is negative', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromAnyPosition, { count: 5, index: -1 });
      expect(result).toBe('bulkModification.validation.countAndIndexRequired');
    });

    it('should return undefined when both count and index are valid', () => {
      const result = validate(BulkModificationStringProcessOperation.RemoveFromAnyPosition, { count: 5, index: 0 });
      expect(result).toBeUndefined();
    });
  });

  describe('ReplaceFromStart operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceFromStart, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when find is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceFromStart, { value: createMockValue() });
      expect(result).toBe('bulkModification.validation.findAndReplaceRequired');
    });

    it('should return error when find is empty string', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceFromStart, {
        find: '',
        value: createMockValue()
      });
      expect(result).toBe('bulkModification.validation.findAndReplaceRequired');
    });

    it('should return error when value is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceFromStart, { find: 'test' });
      expect(result).toBe('bulkModification.validation.findAndReplaceRequired');
    });

    it('should return undefined when both find and value are valid', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceFromStart, {
        find: 'test',
        value: createMockValue()
      });
      expect(result).toBeUndefined();
    });
  });

  describe('ReplaceFromEnd operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceFromEnd, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when find is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceFromEnd, { value: createMockValue() });
      expect(result).toBe('bulkModification.validation.findAndReplaceRequired');
    });

    it('should return undefined when both find and value are valid', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceFromEnd, {
        find: 'test',
        value: createMockValue()
      });
      expect(result).toBeUndefined();
    });
  });

  describe('ReplaceFromAnyPosition operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceFromAnyPosition, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when find is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceFromAnyPosition, { value: createMockValue() });
      expect(result).toBe('bulkModification.validation.findAndReplaceRequired');
    });

    it('should return undefined when both find and value are valid', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceFromAnyPosition, {
        find: 'test',
        value: createMockValue()
      });
      expect(result).toBeUndefined();
    });
  });

  describe('ReplaceWithRegex operation', () => {
    it('should return error when options is undefined', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceWithRegex, undefined);
      expect(result).toBe('bulkModification.validation.optionsRequired');
    });

    it('should return error when find (regex pattern) is missing', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceWithRegex, { value: createMockValue() });
      expect(result).toBe('bulkModification.validation.findAndReplaceRequired');
    });

    it('should return undefined when both find and value are valid', () => {
      const result = validate(BulkModificationStringProcessOperation.ReplaceWithRegex, {
        find: '\\d+',
        value: createMockValue()
      });
      expect(result).toBeUndefined();
    });
  });
});
