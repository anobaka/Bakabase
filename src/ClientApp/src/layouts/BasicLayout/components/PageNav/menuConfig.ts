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
    path: '/medialibrary',
  },
  {
    name: 'Data',
    icon: 'DatabaseOutlined',
    path: '/expandable-2',
    children: [
      {
        name: 'Media library template',
        icon: 'AppstoreAddOutlined',
        path: '/medialibrarytemplate',
      },
      {
        name: 'Synchronization options',
        path: '/synchronizationoptions',
        icon: 'SyncOutlined',
      },
      {
        name: 'Custom property',
        path: '/customproperty',
        icon: 'RadarChartOutlined',
      },
      {
        name: 'Custom component',
        path: '/customcomponent',
        icon: 'ControlOutlined',
      },
      {
        name: 'Extension group',
        path: '/extensiongroup',
        icon: 'UngroupOutlined',
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
      {
        name: 'Media library(Deprec)',
        icon: 'ProductOutlined',
        path: '/category',
      },
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
      {
        name: 'Post parser',
        path: '/postparser',
        icon: 'HeatMapOutlined',
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
