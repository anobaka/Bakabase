const headerMenuConfig = [];

export interface IMenuItem {
  name: string;
  path?: string;
  icon: string;
  children?: IMenuItem[];
}

const asideMenuConfig: IMenuItem[] = [
  {
    name: 'Resource',
    path: '/resource',
    icon: 'PictureOutlined',
  },
  {
    name: 'Media library',
    icon: 'ProductOutlined',
    children: [
      {
        name: 'Category',
        path: '/category',
        icon: 'ClusterOutlined',
      },
      {
        name: 'Custom Component',
        path: '/customcomponent',
        icon: 'ControlOutlined',
      },
      {
        name: 'Favorites',
        path: '/favorites',
        icon: 'StarOutlined',
      },
    ],
  },
  {
    name: 'Data',
    icon: 'DatabaseOutlined',
    children: [
      {
        name: 'Resource property',
        path: '/resourceproperty',
        icon: 'RadarChartOutlined',
      },
      {
        name: 'Custom property',
        path: '/customproperty',
        icon: 'RadarChartOutlined',
      },
      {
        name: 'Tag',
        path: '/tag',
        icon: 'TagsOutlined',
      },
      {
        name: 'Alias',
        path: '/alias',
        icon: 'BranchesOutlined',
      },
      {
        name: 'Text',
        path: '/text',
        icon: 'FieldStringOutlined',
      },
      {
        name: 'Enhancement Records',
        path: '/enhancementrecord',
        icon: 'ThunderboltOutlined',
      },
    ],
  },
  {
    name: 'Tools',
    icon: 'ToolOutlined',
    children: [
      {
        name: 'File Processor',
        path: '/fileprocessor',
        icon: 'FileSyncOutlined',
      },
      {
        name: 'Downloader',
        path: '/downloader',
        icon: 'DownloadOutlined',
      },
      {
        name: 'Bulk modification',
        path: '/bulkmodification',
        icon: 'EditOutlined',
      },
      {
        name: 'Other tools',
        path: '/tools',
        icon: 'ToolOutlined',
      },
    ],
  },
  {
    name: 'System',
    icon: 'SettingOutlined',
    children: [
      {
        name: 'Configuration',
        path: '/configuration',
        icon: 'AppstoreOutlined',
      },

      {
        name: 'Background Task',
        path: '/backgroundtask',
        icon: 'InteractionOutlined',
      },
      {
        name: 'Log',
        path: '/log',
        icon: 'FileTextOutlined',
      },
    ],
  },
  ...(process.env.ICE_CORE_MODE == 'development' ? [{
    name: 'Test',
    path: '/test',
    icon: 'Folderorganizer',
    children: [
      {
        name: 'common',
        path: '/test',
        icon: 'CodepenCircleOutlined',
      },
      {
        name: 'bakaui',
        path: '/test/bakaui',
        icon: 'SketchOutlined',
      },
    ],
  }] : []),
];

export { headerMenuConfig, asideMenuConfig };
