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
    path: '/category',
  },
  {
    name: 'Data',
    icon: 'DatabaseOutlined',
    path: '/expandable-2',
    children: [
      {
        name: 'Synchronization options',
        path: '/synchronizationoptions',
        icon: 'SyncOutlined',
      },
      {
        name: 'Custom component',
        path: '/customcomponent',
        icon: 'ControlOutlined',
      },
      {
        name: 'Custom property',
        path: '/customproperty',
        icon: 'RadarChartOutlined',
      },
      {
        name: 'Bulk modification',
        path: '/bulkmodification2',
        icon: 'FormOutlined',
      },
      // {
      //   name: 'Tag',
      //   path: '/tag',
      //   icon: 'TagsOutlined',
      // },
      {
        name: 'Cache',
        path: '/cache',
        icon: 'DashboardOutlined',
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
        name: 'Play history',
        path: '/playhistory',
        icon: 'HistoryOutlined',
      },
      // {
      //   name: 'Enhancement Records',
      //   path: '/enhancementrecord',
      //   icon: 'ThunderboltOutlined',
      // },
    ],
  },
  {
    name: 'Tools',
    icon: 'ToolOutlined',
    path: '/expandable-3',
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
        name: 'File Mover',
        path: '/filemover',
        icon: 'FileSyncOutlined',
      },
      // {
      //   name: 'Other tools',
      //   path: '/tools',
      //   icon: 'ToolOutlined',
      // },
      // {
      //   name: 'Migration',
      //   path: '/migration',
      //   icon: 'TruckOutlined',
      // },
    ],
  },
  {
    name: 'System',
    icon: 'SettingOutlined',
    path: '/expandable-4',
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
    path: '/expandable-5',
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
      {
        name: 'nextui',
        path: '/test/nextui',
        icon: 'SketchOutlined',
      },
    ],
  }] : []),
];

export { headerMenuConfig, asideMenuConfig };
