import React from 'react';
import ReactDOM from 'react-dom/client';
import { HeroUIProvider } from '@heroui/system';
import { ToastProvider } from '@heroui/toast';
import { App } from './App';
import { exhentaiConfig } from './sites/exhentai/index';
import { soulplusConfig } from './sites/soulplus/index';
import { OVERLAY_ROOT_ID } from './overlay';
import './index.css';

const SITE_CONFIGS = [exhentaiConfig, soulplusConfig];

function boot() {
  // Expose host page's <html> font-size so CSS zoom can compensate for
  // HeroUI's rem-based sizing across sites with different base font sizes.
  const htmlFs = parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
  document.documentElement.style.setProperty('--bk-html-fs', String(htmlFs));

  const rootEl = document.createElement('div');
  rootEl.id = 'bk-app';
  document.body.appendChild(rootEl);

  // Dedicated container for HeroUI overlays so they are not portaled to the
  // bare <body>, where the scoped reset in index.css would not reach them.
  const overlayRoot = document.createElement('div');
  overlayRoot.id = OVERLAY_ROOT_ID;
  document.body.appendChild(overlayRoot);

  ReactDOM.createRoot(rootEl).render(
    <React.StrictMode>
      <HeroUIProvider>
        <ToastProvider placement="top-right" />
        <App siteConfigs={SITE_CONFIGS} />
      </HeroUIProvider>
    </React.StrictMode>,
  );
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', boot);
} else {
  boot();
}
