import { useEffect, useState } from 'react';
import { Button } from '@heroui/button';
import { Input } from '@heroui/input';
import { Popover, PopoverTrigger, PopoverContent } from '@heroui/popover';
import { Select, SelectItem } from '@heroui/select';
import { IoSettingsSharp } from 'react-icons/io5';
import { getApiBaseUrl, setApiBaseUrl, getStoredValue, setStoredValue } from '../api';
import { showToast } from './Toast';
import { t, getLocale, setLocale, onLocaleChange, type Locale } from '../i18n';

const localeOptions: { key: Locale; label: string }[] = [
  { key: 'zh', label: '中文' },
  { key: 'en', label: 'English' },
];

export function SettingsPanel() {
  const [markersVisible, setMarkersVisible] = useState(() => getStoredValue('markers_visible', true));
  const [apiUrl, setApiUrl] = useState(() => getApiBaseUrl());
  const [isOpen, setIsOpen] = useState(false);
  const [locale, setLocaleState] = useState(getLocale);
  const [, forceUpdate] = useState(0);

  useEffect(() => onLocaleChange(() => forceUpdate((n) => n + 1)), []);

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
  };

  const handleLocaleChange = (key: Locale) => {
    setLocaleState(key);
    setLocale(key);
  };

  return (
    <div style={{ position: 'fixed', bottom: 20, right: 20, display: 'flex', flexDirection: 'column', alignItems: 'flex-end', gap: 8, zIndex: 10000 }}>
      <Button
        size="sm"
        color={markersVisible ? 'warning' : 'success'}
        variant="solid"
        onPress={handleToggleMarkers}
      >
        {markersVisible ? t('hideMarkers') : t('showMarkers')}
      </Button>

      <Popover placement="left" isOpen={isOpen} onOpenChange={setIsOpen}>
        <PopoverTrigger>
          <Button size="sm" color="primary" variant="solid" isIconOnly>
            <IoSettingsSharp size={16} />
          </Button>
        </PopoverTrigger>
        <PopoverContent>
          <div className="flex flex-col gap-3 p-3" style={{ width: 300 }}>
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
          </div>
        </PopoverContent>
      </Popover>
    </div>
  );
}
