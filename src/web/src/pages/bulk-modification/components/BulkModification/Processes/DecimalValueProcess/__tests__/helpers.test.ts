import { describe, it, expect } from 'vitest';
import { validate } from '../helpers';
import { BulkModificationDecimalProcessOperation } from '@/sdk/constants';
import type { BulkModificationProcessValue } from '@/pages/bulk-modification/components/BulkModification/models';

// Helper to create a mock process value
const createMockValue = (): BulkModificationProcessValue => ({
  type: 1, // ManuallyInput
  value: '123.45',
});

describe('DecimalValueProcess validate', () => {
  describe('Delete operation', () => {
    it('should return undefined (no error) for Delete operation', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Delete, {});
      expect(result).toBeUndefined();
    });
  });

  describe('Ceil operation', () => {
    it('should return undefined (no error) for Ceil operation', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Ceil, {});
      expect(result).toBeUndefined();
    });

    it('should return undefined even with options', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Ceil, { value: createMockValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('Floor operation', () => {
    it('should return undefined (no error) for Floor operation', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Floor, {});
      expect(result).toBeUndefined();
    });

    it('should return undefined even with options', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Floor, { value: createMockValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('SetWithFixedValue operation', () => {
    it('should return error when value is missing', () => {
      const result = validate(BulkModificationDecimalProcessOperation.SetWithFixedValue, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationDecimalProcessOperation.SetWithFixedValue, { value: createMockValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('Add operation', () => {
    it('should return error when value is missing', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Add, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Add, { value: createMockValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('Subtract operation', () => {
    it('should return error when value is missing', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Subtract, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Subtract, { value: createMockValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('Multiply operation', () => {
    it('should return error when value is missing', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Multiply, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Multiply, { value: createMockValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('Divide operation', () => {
    it('should return error when value is missing', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Divide, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Divide, { value: createMockValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('Round operation', () => {
    it('should return error when decimalPlaces is missing', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Round, {});
      expect(result).toBe('bulkModification.validation.decimalPlacesRequired');
    });

    it('should return error when decimalPlaces is undefined', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Round, { decimalPlaces: undefined });
      expect(result).toBe('bulkModification.validation.decimalPlacesRequired');
    });

    it('should return error when decimalPlaces is negative', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Round, { decimalPlaces: -1 });
      expect(result).toBe('bulkModification.validation.decimalPlacesRequired');
    });

    it('should return undefined when decimalPlaces is 0', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Round, { decimalPlaces: 0 });
      expect(result).toBeUndefined();
    });

    it('should return undefined when decimalPlaces is positive', () => {
      const result = validate(BulkModificationDecimalProcessOperation.Round, { decimalPlaces: 2 });
      expect(result).toBeUndefined();
    });
  });
});
