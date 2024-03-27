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
    cssAssetsLocal(),
  ],
  sourceMap: true,
  // routes: {
  //   // 忽略所有约定式规则
  //   // ignoreFiles: ['**/components/**'],
  //   ignoreFiles: ['**'],
  //   defineRoutes: (route) => {
  //
  //     route('/', '@/layouts/BasicLayout/index.tsx', () => {
  //       // todo: nested '/' route is not working after upgrading @ice/runtime to 1.2.7. Maybe we should switch ice framework to another.
  //       route('/', 'Dashboard/index.tsx');
  //       route('/tools', 'Tools/index.tsx');
  //       route('/alias', 'Alias/index.tsx');
  //       route('/background-task', 'BackgroundTask/index.tsx');
  //       route('/enhancement-record', 'EnhancementRecord/index.tsx');
  //       route('/category/setup-wizard', 'Category/SetupWizard/index.tsx');
  //       route('/category', 'Category/index.tsx');
  //       route('/custom-component', 'CustomComponent/index.tsx');
  //       route('/log', 'Log/index.tsx');
  //       route('/resource', 'Resource2/index.tsx');
  //       route('/configuration', 'Configuration/index.tsx');
  //       route('/tag', 'Tag/index.tsx');
  //       route('/test', 'Test/index.tsx');
  //       route('/text', 'Text/index.tsx');
  //       route('/tools', 'Tools/index.tsx');
  //       route('/file-processor', 'FileProcessor/index.tsx');
  //       route('/favorites', 'Favorites/index.tsx');
  //       route('/downloader', 'Downloader/index.tsx');
  //       route('/bulk-modification', 'BulkModification/index.tsx');
  //       route('/resource-property', 'ResourceProperty/index.tsx');
  //       route('/custom-property', 'CustomProperty/index.tsx');
  //     });
  //     // route('/welcome', '../layouts/BlankLayout/index.tsx', () => {
  //     //   route('/welcome', 'Welcome/index.tsx');
  //     // })
  //   },
  // },
  webpack: (webpackConfig) => {
    return webpackConfig;
  },
}));
