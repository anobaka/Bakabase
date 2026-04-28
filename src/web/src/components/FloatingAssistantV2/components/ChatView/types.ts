export interface ChatConversation {
  id: number;
  title?: string;
  createdAt: string;
  updatedAt: string;
  isArchived: boolean;
}

export interface ChatMessageRecord {
  id: number;
  conversationId: number;
  role: 'system' | 'user' | 'assistant' | 'tool';
  content?: string;
  toolCalls?: string;
  toolResults?: string;
  richContent?: string;
  createdAt: string;
  tokenUsage?: number;
}

export interface ChatStreamEvent {
  type: ChatStreamEventType;
  text?: string;
  toolName?: string;
  toolArgs?: string;
  toolResult?: string;
  richContent?: string;
  error?: string;
}

export enum ChatStreamEventType {
  TextDelta = 1,
  ToolCallStart = 2,
  ToolCallResult = 3,
  Done = 4,
  Error = 5,
}

export interface ChatToolInfo {
  name: string;
  description: string;
  isReadOnly: boolean;
  isEnabled: boolean;
}

// A message in the UI (may be built from streaming)
export interface UiMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
  toolCalls?: { name: string; args?: string; result?: string; isLoading?: boolean }[];
  isStreaming?: boolean;
  error?: string;
}
