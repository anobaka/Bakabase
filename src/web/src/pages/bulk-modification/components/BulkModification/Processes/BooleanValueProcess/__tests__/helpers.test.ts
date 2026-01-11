import { describe, it, expect } from 'vitest';
import { validate } from '../helpers';
import { BulkModificationBooleanProcessOperation } from '@/sdk/constants';
import type { BulkModificationProcessValue } from '@/pages/bulk-modification/components/BulkModification/models';

// Helper to create a mock process value
const createMockValue = (value: boolean): BulkModificationProcessValue => ({
  type: 1, // ManuallyInput
  value: String(value),
});

describe('BooleanValueProcess validate', () => {
  describe('Delete operation', () => {
    it('should return undefined (no error) for Delete operation', () => {
      const result = validate(BulkModificationBooleanProcessOperation.Delete, {});
      expect(result).toBeUndefined();
    });

    it('should return undefined even with options', () => {
      const result = validate(BulkModificationBooleanProcessOperation.Delete, { value: createMockValue(true) });
      expect(result).toBeUndefined();
    });
  });

  describe('Toggle operation', () => {
    it('should return undefined (no error) for Toggle operation', () => {
      const result = validate(BulkModificationBooleanProcessOperation.Toggle, {});
      expect(result).toBeUndefined();
    });

    it('should return undefined even with options', () => {
      const result = validate(BulkModificationBooleanProcessOperation.Toggle, { value: createMockValue(true) });
      expect(result).toBeUndefined();
    });
  });

  describe('SetWithFixedValue operation', () => {
    it('should return error when value is missing', () => {
      const result = validate(BulkModificationBooleanProcessOperation.SetWithFixedValue, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is true', () => {
      const result = validate(BulkModificationBooleanProcessOperation.SetWithFixedValue, { value: createMockValue(true) });
      expect(result).toBeUndefined();
    });

    it('should return undefined when value is false', () => {
      const result = validate(BulkModificationBooleanProcessOperation.SetWithFixedValue, { value: createMockValue(false) });
      expect(result).toBeUndefined();
    });
  });
});
