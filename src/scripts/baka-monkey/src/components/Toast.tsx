import { addToast } from '@heroui/toast';

export function showToast(text: string, isError = false): void {
  addToast({
    title: text,
    color: isError ? 'danger' : 'success',
    timeout: 3000,
  });
}
