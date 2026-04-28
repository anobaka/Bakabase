import envConfig from '@/config/env';
import type { ChatStreamEvent } from './types';

/**
 * Send a chat message via SSE streaming.
 * BApi's generated client doesn't support SSE, so we use fetch directly
 * with the same base URL as BApi.
 */
export function sendChatMessageSSE(
  conversationId: number,
  message: string,
  onEvent: (evt: ChatStreamEvent) => void,
  onError: (err: Error) => void,
): AbortController {
  const controller = new AbortController();
  const baseUrl = envConfig.apiEndpoint || '';
  const url = `${baseUrl}/chat/conversations/${conversationId}/messages`;

  fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ message }),
    signal: controller.signal,
  })
    .then(async (res) => {
      if (!res.ok || !res.body) {
        onError(new Error(`HTTP ${res.status}`));
        return;
      }
      const reader = res.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });

        const lines = buffer.split('\n');
        buffer = lines.pop() ?? '';

        for (const line of lines) {
          const trimmed = line.trim();
          if (trimmed.startsWith('data: ')) {
            try {
              const evt: ChatStreamEvent = JSON.parse(trimmed.slice(6));
              onEvent(evt);
            } catch {
              // skip malformed lines
            }
          }
        }
      }
    })
    .catch((err) => {
      if (err.name !== 'AbortError') {
        onError(err);
      }
    });

  return controller;
}
