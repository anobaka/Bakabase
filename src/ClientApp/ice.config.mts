import { defineConfig } from '@ice/app';
import store from '@ice/plugin-store';
import cssAssetsLocal from '@ice/plugin-css-assets-local';
import * as util from 'util';

// The project config, see https://v3.ice.work/docs/guide/basic/config
export default defineConfig(() => ({
  ssr: false,
  ssg: false,
  plugins: [
    store(),
    // cssAssetsLocal(),
  ],
  postcss: {
    plugins: ['tailwindcss'],
  },
  sourceMap: true,
  webpack: (webpackConfig) => {
    webpackConfig.devServer??={};
    webpackConfig.devServer.client??={};
    webpackConfig.devServer.client.overlay??={};
    const prevRuntimeErrors = webpackConfig.devServer.client.overlay.runtimeErrors;
    webpackConfig.devServer.client.overlay.runtimeErrors = error => {
      if(error?.message === "ResizeObserver loop completed with undelivered notifications.")
      {
        console.error(error)
        return false;
      }
      return prevRuntimeErrors?.(error) ?? true;
    }
    return webpackConfig;
  },
}));
