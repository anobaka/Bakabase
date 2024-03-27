// 菜单配置
// defaultMenuConfig：脚手架默认侧边栏配置
// asideMenuConfig：自定义侧边导航配置

const headerMenuConfig = [];

interface IMenuItem {
  name: string;
  path?: string;
  icon?: string;
  children?: IMenuItem[];
}

const asideMenuConfig: IMenuItem[] = [
  {
    name: 'Resource',
    path: '/resource',
    icon: 'image',
  },
  {
    name: 'Media library',
    children: [
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
        name: 'Favorites',
        path: '/favorites',
        icon: 'star',
      },
    ],
  },
  {
    name: 'Data',
    children: [
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
        name: 'Tag',
        path: '/tag',
        icon: 'tags',
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
        name: 'Enhancement Records',
        path: '/enhancement-record',
        icon: 'flashlight',
      },
    ],
  },
  {
    name: 'Tool',
    icon: 'experiment',
    children: [
      {
        name: 'File Processor',
        path: '/file-processor',
        icon: 'work',
      },
      {
        name: 'Downloader',
        path: '/downloader',
        icon: 'download',
      },
      {
        name: 'Bulk modification',
        path: '/bulk-modification',
        icon: 'modify',
      },
      {
        name: 'Tool',
        path: '/tools',
        icon: 'experiment',
      },
    ],
  },
  {
    name: 'System',
    children: [
      {
        name: 'Configuration',
        path: '/configuration',
        icon: 'setting',
      },

      {
        name: 'Background Task',
        path: '/background-task',
        icon: 'reddit',
      },
      {
        name: 'Log',
        path: '/log',
        icon: 'detail',
      },
    ],
  },
  ...(process.env.ICE_CORE_MODE == 'development' ? [{
    name: 'Test',
    path: '/test',
    icon: 'Folderorganizer',
  }] : []),
];

export { headerMenuConfig, asideMenuConfig };
