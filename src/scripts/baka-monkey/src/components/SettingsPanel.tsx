import { useEffect, useState } from 'react';
import { Button } from '@heroui/button';
import { Input } from '@heroui/input';
import { Popover, PopoverTrigger, PopoverContent } from '@heroui/popover';
import { Select, SelectItem } from '@heroui/select';
import { IoSettingsSharp, IoWarning } from 'react-icons/io5';
import { getApiBaseUrl, setApiBaseUrl, getStoredValue, setStoredValue, httpRequest } from '../api';
import { pingNow } from '../heartbeat';
import { showToast } from './Toast';
import { t, getLocale, setLocale, onLocaleChange, type Locale } from '../i18n';

const localeOptions: { key: Locale; label: string }[] = [
  { key: 'zh', label: '中文' },
  { key: 'en', label: 'English' },
];

export function SettingsPanel({ siteKey, connected }: { siteKey?: string; connected: boolean }) {
  const [markersVisible, setMarkersVisible] = useState(() => getStoredValue('markers_visible', true));
  const [apiUrl, setApiUrl] = useState(() => getApiBaseUrl());
  const [isOpen, setIsOpen] = useState(false);
  const [locale, setLocaleState] = useState(getLocale);
  const [, forceUpdate] = useState(0);
  const [autoBuyThreshold, setAutoBuyThreshold] = useState<string>('');
  const [autoBuyLoading, setAutoBuyLoading] = useState(false);

  useEffect(() => onLocaleChange(() => forceUpdate((n) => n + 1)), []);

  // Fetch soulplus auto-buy threshold when panel opens on soulplus site
  useEffect(() => {
    if (siteKey !== 'soulplus' || !isOpen) return;
    const baseUrl = getApiBaseUrl();
    if (!baseUrl) return;
    httpRequest<{ data?: { autoBuyThreshold?: number } }>({
      method: 'GET',
      url: `${baseUrl}/options/soulplus`,
      onSuccess: (result) => {
        const val = result.data?.autoBuyThreshold;
        setAutoBuyThreshold(val !== undefined ? String(val) : '10');
      },
    });
  }, [siteKey, isOpen]);

  const handleToggleMarkers = () => {
    const next = !markersVisible;
    setMarkersVisible(next);
    setStoredValue('markers_visible', next);

    document.querySelectorAll<HTMLElement>(
      '.bakabase-marker, .bakabase-viewed-overlay, .bakabase-download-btn, .bakabase-parse-btn',
    ).forEach((el) => {
      el.style.display = next ? '' : 'none';
    });
  };

  const handleSaveApiUrl = () => {
    setApiBaseUrl(apiUrl);
    showToast(t('apiUrlSaved'));
    pingNow();
  };

  const handleSaveAutoBuyThreshold = () => {
    const val = parseInt(autoBuyThreshold, 10);
    if (isNaN(val) || val < 0) return;
    setAutoBuyLoading(true);
    httpRequest({
      method: 'PATCH',
      url: `${getApiBaseUrl()}/options/soulplus`,
      data: { autoBuyThreshold: val },
      onSuccess: () => {
        showToast(t('autoBuyThresholdSaved'));
        setAutoBuyLoading(false);
      },
      onError: () => {
        alert(t('requestFailed'));
        setAutoBuyLoading(false);
      },
    });
  };

  const handleLocaleChange = (key: Locale) => {
    setLocaleState(key);
    setLocale(key);
  };

  return (
    <div style={{ position: 'fixed', bottom: 20, right: 20, display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 8, zIndex: 10000 }}>
      {!connected && (
        <div style={{
          display: 'flex',
          alignItems: 'center',
          gap: 6,
          padding: '6px 12px',
          borderRadius: 8,
          background: 'rgba(243,18,96,0.15)',
          color: '#F31260',
          fontSize: 12,
          fontWeight: 600,
          maxWidth: 260,
        }}>
          <IoWarning style={{ flexShrink: 0 }} />
          <span>{t('disconnected')}</span>
        </div>
      )}

      <Popover placement="left" isOpen={isOpen} onOpenChange={setIsOpen}>
        <PopoverTrigger>
          <Button size="sm" color={connected ? 'primary' : 'danger'} variant="solid" isIconOnly>
            <IoSettingsSharp />
          </Button>
        </PopoverTrigger>
        <PopoverContent>
          <div className="flex flex-col gap-3 p-3" style={{ width: 300 }}>
            {!connected && (
              <div style={{
                padding: '8px 10px',
                borderRadius: 8,
                background: 'rgba(243,18,96,0.1)',
                border: '1px solid rgba(243,18,96,0.3)',
                color: '#F31260',
                fontSize: 12,
                lineHeight: 1.5,
              }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 4, fontWeight: 600, marginBottom: 4 }}>
                  <IoWarning />
                  {t('disconnected')}
                </div>
                <div style={{ color: '#666' }}>{t('disconnectedTip')}</div>
              </div>
            )}
            <Button
              size="sm"
              color={markersVisible ? 'warning' : 'success'}
              variant="flat"
              onPress={handleToggleMarkers}
              style={{ width: '100%' }}
            >
              {markersVisible ? t('hideMarkers') : t('showMarkers')}
            </Button>
            <Input
              size="sm"
              label={t('apiUrlLabel')}
              description={t('apiUrlDescription')}
              value={apiUrl}
              onValueChange={setApiUrl}
              onKeyDown={(e) => e.key === 'Enter' && handleSaveApiUrl()}
              endContent={
                <Button size="sm" color="primary" variant="light" onPress={handleSaveApiUrl}>
                  {t('save')}
                </Button>
              }
            />
            <Select
              size="sm"
              label={t('language')}
              selectedKeys={[locale]}
              onSelectionChange={(keys) => {
                const selected = [...keys][0] as Locale;
                if (selected) handleLocaleChange(selected);
              }}
            >
              {localeOptions.map((opt) => (
                <SelectItem key={opt.key}>{opt.label}</SelectItem>
              ))}
            </Select>
            {siteKey === 'soulplus' && (
              <>
                <div style={{ borderTop: '1px solid #444', margin: '4px 0' }} />
                <div style={{ fontSize: 13, fontWeight: 600 }}>{t('soulplusSettings')}</div>
                <Input
                  size="sm"
                  type="number"
                  label={t('autoBuyThresholdLabel')}
                  description={t('autoBuyThresholdDescription')}
                  value={autoBuyThreshold}
                  onValueChange={setAutoBuyThreshold}
                  onKeyDown={(e) => e.key === 'Enter' && handleSaveAutoBuyThreshold()}
                  endContent={
                    <Button
                      size="sm"
                      color="primary"
                      variant="light"
                      isDisabled={autoBuyLoading}
                      onPress={handleSaveAutoBuyThreshold}
                    >
                      {t('save')}
                    </Button>
                  }
                />
              </>
            )}
          </div>
        </PopoverContent>
      </Popover>
    </div>
  );
}
