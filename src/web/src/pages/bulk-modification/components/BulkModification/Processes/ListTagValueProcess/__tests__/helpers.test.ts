import { describe, it, expect } from 'vitest';
import { validate } from '../helpers';
import { BulkModificationListTagProcessOperation } from '@/sdk/constants';

// Helper to create a mock tag value
const createMockTagValue = () => [
  { group: 'TestGroup', name: 'TestTag' }
];

describe('ListTagValueProcess validate', () => {
  describe('Delete operation', () => {
    it('should return undefined (no error) for Delete operation', () => {
      const result = validate(BulkModificationListTagProcessOperation.Delete, {});
      expect(result).toBeUndefined();
    });

    it('should return undefined even with options', () => {
      const result = validate(BulkModificationListTagProcessOperation.Delete, { value: createMockTagValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('SetWithFixedValue operation', () => {
    it('should return error when value is missing', () => {
      const result = validate(BulkModificationListTagProcessOperation.SetWithFixedValue, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationListTagProcessOperation.SetWithFixedValue, { value: createMockTagValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('Append operation', () => {
    it('should return error when value is missing', () => {
      const result = validate(BulkModificationListTagProcessOperation.Append, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationListTagProcessOperation.Append, { value: createMockTagValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('Prepend operation', () => {
    it('should return error when value is missing', () => {
      const result = validate(BulkModificationListTagProcessOperation.Prepend, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationListTagProcessOperation.Prepend, { value: createMockTagValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('Remove operation', () => {
    it('should return error when value is missing', () => {
      const result = validate(BulkModificationListTagProcessOperation.Remove, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationListTagProcessOperation.Remove, { value: createMockTagValue() });
      expect(result).toBeUndefined();
    });
  });
});
