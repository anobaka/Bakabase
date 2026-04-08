import React from 'react';
import ReactDOM from 'react-dom/client';
import { HeroUIProvider } from '@heroui/system';
import { ToastProvider } from '@heroui/toast';
import { App } from './App';
import { exhentaiConfig } from './sites/exhentai';
import { soulplusConfig } from './sites/soulplus';
import './index.css';

const SITE_CONFIGS = [exhentaiConfig, soulplusConfig];

function boot() {
  const rootEl = document.createElement('div');
  rootEl.id = 'bakabase-monkey-root';
  document.body.appendChild(rootEl);

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
