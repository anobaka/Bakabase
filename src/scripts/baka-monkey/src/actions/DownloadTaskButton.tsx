import { useState } from 'react';
import { Button } from '@heroui/button';
import { Tooltip } from '@heroui/tooltip';
import { MdOutlineFileDownload } from 'react-icons/md';
import type { DownloadTaskAdapter } from '../types';
import { showToast } from '../components/Toast';
import { t } from '../i18n';

export function DownloadTaskButton({ adapter, element }: { adapter: DownloadTaskAdapter; element: HTMLElement }) {
  const [loading, setLoading] = useState(false);
  const [hovered, setHovered] = useState(false);

  const handleClick = async () => {
    const url = adapter.extractUrl(element);
    if (!url) {
      alert(t('downloadLinkNotFound'));
      return;
    }
    setLoading(true);
    try {
      await adapter.createTask(url);
      showToast(t('addedToDownloadQueue'));
    } catch {
      alert(t('downloadFailed'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <Tooltip content={t('download')} placement="top" size="sm" color="foreground">
      <Button
        size="sm"
        color="primary"
        variant={!loading && hovered ? 'solid' : 'flat'}
        isIconOnly
        isDisabled={loading}
        onPress={handleClick}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
      >
        <MdOutlineFileDownload size="1.2em" />
      </Button>
    </Tooltip>
  );
}
