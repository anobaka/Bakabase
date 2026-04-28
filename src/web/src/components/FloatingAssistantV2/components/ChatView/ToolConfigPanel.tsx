import React, { useEffect, useState, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { Switch, Chip } from '@/components/bakaui';
import BApi from '@/sdk/BApi';
import type { ChatToolInfo } from './types';

interface Props {
  /** If true, renders as a compact inline section (for chat popover). */
  compact?: boolean;
  /** If true, hides the "Tools" title. */
  hideTitle?: boolean;
}

const ToolConfigPanel: React.FC<Props> = ({ compact = false, hideTitle = false }) => {
  const { t } = useTranslation();
  const [tools, setTools] = useState<ChatToolInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);

  useEffect(() => {
    BApi.chat.getChatTools()
      .then((rsp: any) => {
        if (rsp?.data) setTools(rsp.data);
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false));
  }, []);

  const handleToggle = useCallback(async (toolName: string, isEnabled: boolean) => {
    setTools((prev) =>
      prev.map((item) => (item.name === toolName ? { ...item, isEnabled } : item)),
    );
    try {
      await BApi.chat.setChatToolEnabled(toolName, { isEnabled });
    } catch {
      setTools((prev) =>
        prev.map((item) => (item.name === toolName ? { ...item, isEnabled: !isEnabled } : item)),
      );
    }
  }, []);

  if (loading) {
    return <div className="text-sm text-default-400 p-2">{t('common.state.loading')}</div>;
  }

  if (error) {
    return <div className="text-sm text-default-400 p-2">{t('bakaChat.tools.loadError')}</div>;
  }

  if (tools.length === 0) {
    return <div className="text-sm text-default-400 p-2">{t('bakaChat.tools.empty')}</div>;
  }

  return (
    <div className="flex flex-col gap-2">
      {!hideTitle && (
        <div className={compact ? 'text-xs font-semibold text-default-500' : 'text-sm font-semibold mb-1'}>
          {t('bakaChat.tools.label')}
        </div>
      )}
      <div className={compact ? 'grid grid-cols-1 gap-1' : 'grid grid-cols-2 gap-2'}>
        {tools.map((tool) => (
          <div
            key={tool.name}
            className="flex items-center justify-between gap-2 py-2 px-3 rounded-lg bg-default-50"
          >
            <div className="flex flex-col gap-0.5 min-w-0">
              <div className="flex items-center gap-1.5">
                <span className="text-sm font-medium truncate">
                  {t(`bakaChat.tools.registry.${tool.name}.name`, { defaultValue: tool.name })}
                </span>
                <Chip
                  size="sm"
                  variant="flat"
                  color={tool.isReadOnly ? 'success' : 'warning'}
                >
                  {tool.isReadOnly ? t('bakaChat.tools.readOnly') : t('bakaChat.tools.canModify')}
                </Chip>
              </div>
              <span className="text-xs text-default-500 font-mono truncate" title={tool.name}>
                {tool.name}
              </span>
              <span className="text-xs text-default-400 truncate">
                {t(`bakaChat.tools.registry.${tool.name}.desc`, { defaultValue: tool.description })}
              </span>
            </div>
            <Switch
              size="sm"
              isSelected={tool.isEnabled}
              onValueChange={(v) => handleToggle(tool.name, v)}
            />
          </div>
        ))}
      </div>
    </div>
  );
};

export default React.memo(ToolConfigPanel);
