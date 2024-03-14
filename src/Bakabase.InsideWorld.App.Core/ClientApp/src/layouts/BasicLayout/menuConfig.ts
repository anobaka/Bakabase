// 菜单配置
// defaultMenuConfig：脚手架默认侧边栏配置
// asideMenuConfig：自定义侧边导航配置

const headerMenuConfig = [];

const asideMenuConfig = [
  {
    name: 'Category',
    path: '/category',
    icon: 'block',
  },
  {
    name: 'Custom Component',
    path: '/custom-component',
    icon: 'USB',
  },
  {
    name: 'Tag',
    path: '/tag',
    icon: 'tags',
  },
  {
    name: 'Favorites',
    path: '/favorites',
    icon: 'star',
  },
  {
    name: 'Resource',
    path: '/resource',
    icon: 'image',
  },
  {
    name: 'Tool',
    path: '/tools',
    icon: 'experiment',
  },
  // {
  //   name: 'Subscription',
  //   path: '/subscription',
  //   icon: 'wifi',
  // },
  {
    name: 'Bulk modification',
    path: '/bulk-modification',
    icon: 'modify',
  },
  {
    name: 'File Processor',
    path: '/file-processor',
    icon: 'work',
  },
  {
    name: 'Resource property',
    path: '/resource-property',
    icon: 'database',
  },
  {
    name: 'Custom property',
    path: '/custom-property',
    icon: 'database',
  },
  {
    name: 'Downloader',
    path: '/downloader',
    icon: 'download',
  },
  {
    name: 'Configuration',
    path: '/configuration',
    icon: 'setting',
  },
  {
    name: 'Alias',
    path: '/alias',
    icon: 'block',
  },
  {
    name: 'Text',
    path: '/text',
    icon: 'font-size',
  },
  {
    name: 'Background Task',
    path: '/background-task',
    icon: 'reddit',
  },
  {
    name: 'Enhancement Records',
    path: '/enhancement-record',
    icon: 'flashlight',
  },
  {
    name: 'Log',
    path: '/log',
    icon: 'detail',
  },
  ...(process.env.ICE_CORE_MODE == 'development' ? [{
    name: 'Test',
    path: '/test',
    icon: 'Folderorganizer',
  }] : []),
];

export { headerMenuConfig, asideMenuConfig };
