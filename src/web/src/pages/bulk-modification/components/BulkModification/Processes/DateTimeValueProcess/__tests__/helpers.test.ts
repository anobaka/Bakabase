import { describe, it, expect } from 'vitest';
import { validate } from '../helpers';
import { BulkModificationDateTimeProcessOperation } from '@/sdk/constants';
import type { BulkModificationProcessValue } from '@/pages/bulk-modification/components/BulkModification/models';

// Helper to create a mock process value
const createMockValue = (): BulkModificationProcessValue => ({
  type: 1, // ManuallyInput
  value: '2024-01-15T12:00:00',
});

describe('DateTimeValueProcess validate', () => {
  describe('Delete operation', () => {
    it('should return undefined (no error) for Delete operation', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.Delete, {});
      expect(result).toBeUndefined();
    });
  });

  describe('SetToNow operation', () => {
    it('should return undefined (no error) for SetToNow operation', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.SetToNow, {});
      expect(result).toBeUndefined();
    });

    it('should return undefined even with options', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.SetToNow, { value: createMockValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('SetWithFixedValue operation', () => {
    it('should return error when value is missing', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.SetWithFixedValue, {});
      expect(result).toBe('bulkModification.validation.valueRequired');
    });

    it('should return undefined when value is provided', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.SetWithFixedValue, { value: createMockValue() });
      expect(result).toBeUndefined();
    });
  });

  describe('AddDays operation', () => {
    it('should return error when amount is undefined', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.AddDays, {});
      expect(result).toBe('bulkModification.validation.amountRequired');
    });

    it('should return error when amount is negative', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.AddDays, { amount: -1 });
      expect(result).toBe('bulkModification.validation.amountRequired');
    });

    it('should return undefined when amount is 0', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.AddDays, { amount: 0 });
      expect(result).toBeUndefined();
    });

    it('should return undefined when amount is positive', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.AddDays, { amount: 30 });
      expect(result).toBeUndefined();
    });
  });

  describe('SubtractDays operation', () => {
    it('should return error when amount is undefined', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.SubtractDays, {});
      expect(result).toBe('bulkModification.validation.amountRequired');
    });

    it('should return error when amount is negative', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.SubtractDays, { amount: -1 });
      expect(result).toBe('bulkModification.validation.amountRequired');
    });

    it('should return undefined when amount is valid', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.SubtractDays, { amount: 7 });
      expect(result).toBeUndefined();
    });
  });

  describe('AddMonths operation', () => {
    it('should return error when amount is undefined', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.AddMonths, {});
      expect(result).toBe('bulkModification.validation.amountRequired');
    });

    it('should return error when amount is negative', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.AddMonths, { amount: -1 });
      expect(result).toBe('bulkModification.validation.amountRequired');
    });

    it('should return undefined when amount is valid', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.AddMonths, { amount: 6 });
      expect(result).toBeUndefined();
    });
  });

  describe('SubtractMonths operation', () => {
    it('should return error when amount is undefined', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.SubtractMonths, {});
      expect(result).toBe('bulkModification.validation.amountRequired');
    });

    it('should return undefined when amount is valid', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.SubtractMonths, { amount: 3 });
      expect(result).toBeUndefined();
    });
  });

  describe('AddYears operation', () => {
    it('should return error when amount is undefined', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.AddYears, {});
      expect(result).toBe('bulkModification.validation.amountRequired');
    });

    it('should return error when amount is negative', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.AddYears, { amount: -1 });
      expect(result).toBe('bulkModification.validation.amountRequired');
    });

    it('should return undefined when amount is valid', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.AddYears, { amount: 1 });
      expect(result).toBeUndefined();
    });
  });

  describe('SubtractYears operation', () => {
    it('should return error when amount is undefined', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.SubtractYears, {});
      expect(result).toBe('bulkModification.validation.amountRequired');
    });

    it('should return undefined when amount is valid', () => {
      const result = validate(BulkModificationDateTimeProcessOperation.SubtractYears, { amount: 2 });
      expect(result).toBeUndefined();
    });
  });
});
