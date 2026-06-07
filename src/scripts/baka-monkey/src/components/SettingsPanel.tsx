import { useEffect, useMemo, useState } from 'react';
import { Button } from '@heroui/button';
import { Input } from '@heroui/input';
import { Popover, PopoverTrigger, PopoverContent } from '@heroui/popover';
import { Select, SelectItem } from '@heroui/select';
import { IoSettingsSharp, IoWarning } from 'react-icons/io5';
import { getApiBaseUrl, setApiBaseUrl, getStoredValue, setStoredValue, httpRequest } from '../api';
import { pingNow } from '../heartbeat';
import { getOverlayRoot } from '../overlay';
import {
  AUTO_TIMEZONE,
  getBrowserTimeZone,
  getSupportedTimeZones,
  getTimeZonePreference,
  setTimeZonePreference,
} from '../timezone';
import { showToast } from './Toast';
import { t, getLocale, setLocale, onLocaleChange, type Locale } from '../i18n';

const localeOptions: { key: Locale; label: string }[] = [
  { key: 'zh', label: '中文' },
  { key: 'en', label: 'English' },
];

/** Toggle class on <body>; CSS in index.css hides markers + viewed borders. */
const HIDE_MARKERS_CLASS = 'bk-hide-markers';

// New storage key on purpose: the previous "hide markers" toggle was broken
// (it targeted stale `.bakabase-*` selectors), so it could persist
// `markers_visible: false` while never actually hiding anything. Reading the
// old key now would surprise those users by hiding everything. A fresh key
// resets everyone to "visible" and the working toggle persists from here on.
const MARKERS_VISIBLE_KEY = 'markers_visible_v2';

export function SettingsPanel({ siteKey, connected }: { siteKey?: string; connected: boolean }) {
  const [markersVisible, setMarkersVisible] = useState(() => getStoredValue(MARKERS_VISIBLE_KEY, true));
  const [apiUrl, setApiUrl] = useState(() => getApiBaseUrl());
  const [isOpen, setIsOpen] = useState(false);
  const [locale, setLocaleState] = useState(getLocale);
  const [timeZone, setTimeZoneState] = useState(getTimeZonePreference);
  const [, forceUpdate] = useState(0);
  const [autoBuyThreshold, setAutoBuyThreshold] = useState<string>('');
  const [autoBuyLoading, setAutoBuyLoading] = useState(false);

  const browserTimeZone = useMemo(() => getBrowserTimeZone(), []);
  const timeZoneOptions = useMemo(() => getSupportedTimeZones(), []);

  useEffect(() => onLocaleChange(() => forceUpdate((n) => n + 1)), []);

  // Reflect the persisted markers-visible preference onto <body> on mount.
  useEffect(() => {
    document.body.classList.toggle(HIDE_MARKERS_CLASS, !markersVisible);
  }, [markersVisible]);

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
    // The effect above syncs the `.bk-hide-markers` body class (which index.css
    // uses to hide our buttons and viewed borders) whenever this state changes.
    const next = !markersVisible;
    setMarkersVisible(next);
    setStoredValue(MARKERS_VISIBLE_KEY, next);
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

  const handleTimeZoneChange = (key: string) => {
    setTimeZoneState(key);
    setTimeZonePreference(key);
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

      <Popover placement="left" isOpen={isOpen} onOpenChange={setIsOpen} portalContainer={getOverlayRoot()}>
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
              portalContainer={getOverlayRoot()}
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
            <Select
              size="sm"
              label={t('timezone')}
              description={t('timezoneDescription')}
              portalContainer={getOverlayRoot()}
              selectedKeys={[timeZone]}
              onSelectionChange={(keys) => {
                const selected = [...keys][0] as string;
                if (selected) handleTimeZoneChange(selected);
              }}
            >
              {[
                <SelectItem key={AUTO_TIMEZONE}>
                  {t('timezoneAuto', { tz: browserTimeZone })}
                </SelectItem>,
                ...timeZoneOptions.map((tz) => <SelectItem key={tz}>{tz}</SelectItem>),
              ]}
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
