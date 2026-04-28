import React, { useState, useRef, useCallback, useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import Markdown from 'react-markdown';
import { SettingOutlined, WarningOutlined, CloseOutlined, ExpandAltOutlined, DeleteOutlined } from '@ant-design/icons';
import { AiOutlinePlusCircle } from 'react-icons/ai';
import { Button, Divider, Popover, Tooltip } from '@/components/bakaui';
import { AiFeature, ResourceAdditionalItem } from '@/sdk/constants';
import AiFeatureConfigShortcut from '@/components/AiFeatureConfigShortcut';
import BApi from '@/sdk/BApi';
import useAiFeatureConfigured from '@/hooks/useAiFeatureConfigured';
import type { Resource as ResourceModel } from '@/core/models/Resource';
import ResourceCard from '@/components/Resource';
import ToolConfigPanel from './ToolConfigPanel';
import type { ChatConversation, UiMessage, ChatStreamEvent } from './types';
import { ChatStreamEventType } from './types';
import { sendChatMessageSSE } from './sseHelper';
import './index.scss';

interface Props {
  /** 'popover' = small floating panel, 'page' = full screen page */
  mode: 'popover' | 'page';
  /** Close handler – only used in popover mode */
  onClose?: () => void;
}

/** Tool names that return resource data */
const resourceToolNames = new Set(['GetResourceDetail', 'GetResourcesByIds', 'SearchResources']);

/** Extract resource IDs from a tool result JSON string */
function extractResourceIds(toolName: string, resultJson: string): number[] {
  try {
    const data = JSON.parse(resultJson);
    if (toolName === 'GetResourceDetail') {
      return data?.Id != null ? [data.Id] : [];
    }
    if (toolName === 'GetResourcesByIds') {
      return Array.isArray(data) ? data.map((r: any) => r.Id).filter((id: any) => id != null) : [];
    }
    if (toolName === 'SearchResources') {
      return Array.isArray(data?.Items) ? data.Items.map((r: any) => r.Id).filter((id: any) => id != null) : [];
    }
  } catch { /* ignore */ }
  return [];
}

/** Inline component that fetches and renders resource cards for tool results */
const ToolResourceCards: React.FC<{ toolName: string; result: string }> = ({ toolName, result }) => {
  const [resources, setResources] = useState<ResourceModel[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const ids = extractResourceIds(toolName, result);
    if (ids.length === 0) {
      setLoading(false);
      return;
    }
    BApi.resource.getResourcesByKeys({ ids, additionalItems: ResourceAdditionalItem.All }).then((rsp: any) => {
      if (rsp?.data) {
        setResources(rsp.data);
      }
    }).catch(() => {}).finally(() => setLoading(false));
  }, [toolName, result]);

  if (loading) return <div className="baka-chat-resource-loading">...</div>;
  if (resources.length === 0) return null;

  return (
    <div className="baka-chat-resource-cards">
      {resources.map((r) => (
        <ResourceCard key={r.id} resource={r} disableCoverClick={false} />
      ))}
    </div>
  );
};

const ChatView: React.FC<Props> = ({ mode, onClose }) => {
  const { t } = useTranslation();
  const { isConfigured: modelConfigured, isLoading: modelLoading } = useAiFeatureConfigured(AiFeature.Chat);
  const [conversations, setConversations] = useState<ChatConversation[]>([]);
  const [activeConversationId, setActiveConversationId] = useState<number | null>(null);
  const [messages, setMessages] = useState<UiMessage[]>([]);
  const [input, setInput] = useState('');
  const [isSending, setIsSending] = useState(false);
  const [configOpen, setConfigOpen] = useState(false);
  const [warningDismissed, setWarningDismissed] = useState(() => {
    try { return localStorage.getItem('baka-chat-warning-dismissed') === '1'; } catch { return false; }
  });
  const listRef = useRef<HTMLDivElement>(null);
  const abortRef = useRef<AbortController | null>(null);
  const skipNextLoadRef = useRef(false);

  const loadConversations = useCallback(() => {
    BApi.chat.getChatConversations().then((rsp: any) => {
      if (rsp?.data) setConversations(rsp.data);
    }).catch(() => {});
  }, []);

  // Load conversations on mount
  useEffect(() => {
    loadConversations();
  }, [loadConversations]);

  // Load messages when active conversation changes
  useEffect(() => {
    if (activeConversationId == null) {
      setMessages([]);
      return;
    }
    // Skip loading when we just created the conversation during send –
    // messages are already in local state and the server may not have them yet.
    if (skipNextLoadRef.current) {
      skipNextLoadRef.current = false;
      return;
    }
    BApi.chat.getChatMessages(activeConversationId).then((rsp: any) => {
      if (rsp?.data) {
        setMessages(
          rsp.data
            .filter((r: any) => r.role === 'user' || r.role === 'assistant')
            .map((r: any) => {
              const msg: UiMessage = {
                id: String(r.id),
                role: r.role as 'user' | 'assistant',
                content: r.content ?? '',
              };
              // Restore persisted tool calls & results
              if (r.toolCalls) {
                try {
                  const calls: { name: string; args?: string }[] = JSON.parse(r.toolCalls);
                  const results: { name: string; result?: string }[] = r.toolResults ? JSON.parse(r.toolResults) : [];
                  msg.toolCalls = calls.map((tc, i) => ({
                    name: tc.name,
                    args: tc.args,
                    result: results[i]?.result,
                    isLoading: false,
                  }));
                } catch { /* ignore malformed JSON */ }
              }
              return msg;
            }),
        );
      }
    }).catch(() => {});
  }, [activeConversationId]);

  // Auto-scroll
  useEffect(() => {
    if (listRef.current) {
      listRef.current.scrollTop = listRef.current.scrollHeight;
    }
  }, [messages]);

  const handleSend = useCallback(async () => {
    const text = input.trim();
    if (!text || isSending) return;

    let convId = activeConversationId;
    if (convId == null) {
      try {
        const rsp: any = await BApi.chat.createChatConversation();
        if (rsp?.data) {
          convId = rsp.data.id;
          skipNextLoadRef.current = true;
          setActiveConversationId(convId);
          setConversations((prev) => [rsp.data, ...prev]);
        } else {
          return;
        }
      } catch {
        return;
      }
    }

    const userMsg: UiMessage = {
      id: `u-${Date.now()}`,
      role: 'user',
      content: text,
    };
    setMessages((prev) => [...prev, userMsg]);
    setInput('');
    setIsSending(true);

    const assistantId = `a-${Date.now()}`;
    setMessages((prev) => [...prev, {
      id: assistantId,
      role: 'assistant',
      content: '',
      isStreaming: true,
      toolCalls: [],
    }]);

    // SSE streaming (BApi doesn't support SSE natively, use helper)
    abortRef.current = sendChatMessageSSE(
      convId!,
      text,
      (evt) => {
        setMessages((prev) => {
          const msgs = [...prev];
          const idx = msgs.findIndex((m) => m.id === assistantId);
          if (idx === -1) return prev;
          const msg = { ...msgs[idx] };

          switch (evt.type) {
            case ChatStreamEventType.TextDelta:
              msg.content += evt.text ?? '';
              break;
            case ChatStreamEventType.ToolCallStart:
              msg.toolCalls = [
                ...(msg.toolCalls ?? []),
                { name: evt.toolName ?? '', args: evt.toolArgs, isLoading: true },
              ];
              break;
            case ChatStreamEventType.ToolCallResult: {
              const calls = [...(msg.toolCalls ?? [])];
              const lastCall = [...calls].reverse().find((c: { name: string }) => c.name === evt.toolName);
              if (lastCall) {
                lastCall.result = evt.toolResult;
                lastCall.isLoading = false;
              }
              msg.toolCalls = calls;
              break;
            }
            case ChatStreamEventType.Done:
              msg.isStreaming = false;
              break;
            case ChatStreamEventType.Error:
              msg.isStreaming = false;
              msg.error = evt.error;
              break;
          }

          msgs[idx] = msg;
          return msgs;
        });
      },
      (err) => {
        setMessages((prev) => {
          const msgs = [...prev];
          const idx = msgs.findIndex((m) => m.id === assistantId);
          if (idx !== -1) {
            msgs[idx] = { ...msgs[idx], isStreaming: false, error: err.message };
          }
          return msgs;
        });
        setIsSending(false);
      },
    );

    const checkDone = setInterval(() => {
      setMessages((prev) => {
        const msg = prev.find((m) => m.id === assistantId);
        if (msg && !msg.isStreaming) {
          clearInterval(checkDone);
          setIsSending(false);
          loadConversations();
        }
        return prev;
      });
    }, 500);
  }, [input, isSending, activeConversationId, loadConversations]);

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        handleSend();
      }
    },
    [handleSend],
  );

  const handleNewConversation = useCallback(() => {
    setActiveConversationId(null);
    setMessages([]);
  }, []);

  const handleDeleteConversation = useCallback((e: React.MouseEvent, convId: number) => {
    e.stopPropagation();
    BApi.chat.deleteChatConversation(convId).then(() => {
      setConversations((prev) => prev.filter((c) => c.id !== convId));
      if (activeConversationId === convId) {
        setActiveConversationId(null);
        setMessages([]);
      }
    }).catch(() => {});
  }, [activeConversationId]);

  const handleDismissWarning = useCallback(() => {
    setWarningDismissed(true);
    try { localStorage.setItem('baka-chat-warning-dismissed', '1'); } catch { /* */ }
  }, []);

  const handleNavigateToPage = useCallback(() => {
    window.location.hash = '#/ai/chat';
    onClose?.();
  }, [onClose]);

  const isPage = mode === 'page';

  return (
    <div className={`baka-chat-view baka-chat-view-${mode}`}>
      {/* Sidebar - conversation list (page mode only) */}
      {isPage && (
        <div className="baka-chat-sidebar">
          <div className="baka-chat-sidebar-header">
            <span className="baka-chat-sidebar-title">{t('bakaChat.conversations.label')}</span>
            <Button
              size="sm"
              variant="light"
              isIconOnly
              aria-label={t('bakaChat.newConversation.label')}
              onPress={handleNewConversation}
            >
              <AiOutlinePlusCircle style={{ fontSize: 22 }} />
            </Button>
          </div>
          <div className="baka-chat-sidebar-list">
            {conversations.map((conv) => (
              <div
                key={conv.id}
                className={`baka-chat-sidebar-item ${conv.id === activeConversationId ? 'active' : ''}`}
                onClick={() => setActiveConversationId(conv.id)}
              >
                <span className="baka-chat-sidebar-item-title">
                  {conv.title || t('bakaChat.newConversation.label')}
                </span>
                <Tooltip content={t('bakaChat.deleteConversation.tooltip')}>
                  <button
                    className="baka-chat-sidebar-item-delete"
                    onClick={(e) => handleDeleteConversation(e, conv.id)}
                  >
                    <DeleteOutlined />
                  </button>
                </Tooltip>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Main chat area */}
      <div className="baka-chat-main">
        <div className="baka-chat-header">
          <span className="baka-chat-title">{t('bakaChat.title')}</span>
          <div className="baka-chat-header-actions">
            <Popover
              visible={configOpen}
              onOpenChange={setConfigOpen}
              trigger={
                <button
                  className="baka-chat-icon-btn"
                  title={t('bakaChat.config.tooltip')}
                >
                  <SettingOutlined />
                </button>
              }
            >
              <div className="baka-chat-config-panel" onClick={(e) => e.stopPropagation()}>
                <div className="flex flex-col gap-2 p-3">
                  <span className="text-sm font-semibold">{t('bakaChat.config.model.label')}</span>
                  <AiFeatureConfigShortcut feature={AiFeature.Chat} />
                </div>
                <Divider orientation="horizontal" />
                <div className="p-3">
                  <ToolConfigPanel compact />
                </div>
              </div>
            </Popover>
            {!isPage && (
              <>
                <Tooltip content={t('bakaChat.openFullPage.tooltip')}>
                  <button
                    className="baka-chat-icon-btn"
                    onClick={handleNavigateToPage}
                  >
                    <ExpandAltOutlined />
                  </button>
                </Tooltip>
                <button className="baka-chat-icon-btn" onClick={onClose}>
                  <CloseOutlined />
                </button>
              </>
            )}
          </div>
        </div>

        {/* AI safety warning – dismissible */}
        {modelConfigured && !warningDismissed && (
          <div className="baka-chat-warning">
            <WarningOutlined className="baka-chat-warning-icon" />
            <span>{t('bakaChat.warning.aiMayMakeErrors')}</span>
            <button className="baka-chat-warning-close" onClick={handleDismissWarning}>
              <CloseOutlined />
            </button>
          </div>
        )}

        {!modelLoading && !modelConfigured ? (
          <div className="baka-chat-messages">
            <div className="baka-chat-empty baka-chat-not-configured">
              <WarningOutlined style={{ fontSize: 24, marginBottom: 8 }} />
              <span>{t('bakaChat.notConfigured.label')}</span>
              <AiFeatureConfigShortcut feature={AiFeature.Chat} />
            </div>
          </div>
        ) : (
          <>
            <div className="baka-chat-messages" ref={listRef}>
              {messages.length === 0 && (
                <div className="baka-chat-empty">
                  <span>{t('bakaChat.empty.label')}</span>
                </div>
              )}
              {messages.map((msg) => (
                <div key={msg.id} className={`baka-chat-msg baka-chat-msg-${msg.role}`}>
                  <div className="baka-chat-bubble baka-chat-markdown">
                    {msg.role === 'assistant' ? (
                      <Markdown>{msg.content}</Markdown>
                    ) : (
                      msg.content
                    )}
                    {msg.isStreaming && <span className="baka-chat-cursor">|</span>}
                  </div>
                  {msg.toolCalls?.map((tc, i) => {
                    let parsedArgs: Record<string, unknown> | null = null;
                    if (tc.args) {
                      try { parsedArgs = JSON.parse(tc.args); } catch { /* ignore */ }
                    }
                    const isResourceTool = resourceToolNames.has(tc.name);
                    return (
                      <div key={i} className="baka-chat-tool-call">
                        <span className="baka-chat-tool-name">
                          {tc.isLoading ? '⏳' : '✓'}{' '}
                          {t(`bakaChat.tools.registry.${tc.name}.name`, { defaultValue: tc.name })}
                        </span>
                        {parsedArgs && Object.keys(parsedArgs).length > 0 && (
                          <span className="baka-chat-tool-args">
                            {Object.entries(parsedArgs).map(([k, v]) => (
                              <span key={k} className="baka-chat-tool-arg">
                                <span className="baka-chat-tool-arg-key">{k}</span>
                                <span className="baka-chat-tool-arg-value">{String(v)}</span>
                              </span>
                            ))}
                          </span>
                        )}
                        {tc.result && isResourceTool && !tc.isLoading && (
                          <ToolResourceCards toolName={tc.name} result={tc.result} />
                        )}
                        {tc.result && (
                          <details className="baka-chat-tool-result">
                            <summary>{t('bakaChat.toolResult.label')}</summary>
                            <pre>{tc.result}</pre>
                          </details>
                        )}
                      </div>
                    );
                  })}
                  {msg.error && (
                    <div className="baka-chat-error">{msg.error}</div>
                  )}
                </div>
              ))}
            </div>

            <div className="baka-chat-input-area">
              <input
                type="text"
                className="baka-chat-input"
                value={input}
                onChange={(e) => setInput(e.target.value)}
                onKeyDown={handleKeyDown}
                placeholder={t('bakaChat.input.placeholder')}
                disabled={isSending}
              />
              <button
                className="baka-chat-send"
                onClick={handleSend}
                disabled={isSending || !input.trim()}
              >
                &#10148;
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
};

export default React.memo(ChatView);
