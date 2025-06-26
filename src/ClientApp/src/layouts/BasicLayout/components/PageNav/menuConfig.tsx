import {
  AiOutlineAppstore,
  AiOutlineAppstoreAdd,
  AiOutlineBranches,
  AiOutlineCodepenCircle,
  AiOutlineControl,
  AiOutlineDashboard,
  AiOutlineDatabase,
  AiOutlineDownload,
  AiOutlineExperiment,
  AiOutlineFieldString,
  AiOutlineFileSync,
  AiOutlineFileText,
  AiOutlineForm,
  AiOutlineHeatMap,
  AiOutlineHistory,
  AiOutlineInteraction,
  AiOutlinePicture,
  AiOutlineProduct,
  AiOutlineRadarChart,
  AiOutlineSetting,
  AiOutlineSketch,
  AiOutlineSync,
  AiOutlineTool,
  AiOutlineUngroup,
} from 'react-icons/ai';
import type { IconType } from 'react-icons';
import { MdOutlineIntegrationInstructions, MdOutlineTopic } from 'react-icons/md';

const headerMenuConfig = [];

export interface IMenuItem {
  name: string;
  path?: string;
  icon?: IconType;
  children?: IMenuItem[];
}

const asideMenuConfig: IMenuItem[] = [
  {
    name: 'Resource',
    path: '/resource',
    icon: AiOutlinePicture,
  },
  {
    name: 'Media library',
    icon: AiOutlineProduct,
    path: '/medialibrary',
  },
  {
    name: 'Data',
    icon: AiOutlineDatabase,
    path: '/expandable-2',
    children: [
      {
        name: 'Media library template',
        icon: AiOutlineAppstoreAdd,
        path: '/medialibrarytemplate',
      },
      {
        name: 'Synchronization options',
        path: '/synchronizationoptions',
        icon: AiOutlineSync,
      },
      {
        name: 'Custom property',
        path: '/customproperty',
        icon: AiOutlineRadarChart,
      },
      {
        name: 'Extension group',
        path: '/extensiongroup',
        icon: AiOutlineUngroup,
      },
      {
        name: 'Bulk modification',
        path: '/bulkmodification2',
        icon: AiOutlineForm,
      },
      // {
      //   name: 'Tag',
      //   path: '/tag',
      //   icon: AiOutlineTags,
      // },
      {
        name: 'Cache',
        path: '/cache',
        icon: AiOutlineDashboard,
      },
      {
        name: 'Alias',
        path: '/alias',
        icon: AiOutlineBranches,
      },
      {
        name: 'Text',
        path: '/text',
        icon: AiOutlineFieldString,
      },
      {
        name: 'Play history',
        path: '/playhistory',
        icon: AiOutlineHistory,
      },
      // {
      //   name: 'Enhancement Records',
      //   path: '/enhancementrecord',
      //   icon: AiOutlineThunderbolt,
      // },
      {
        name: 'Custom component',
        path: '/customcomponent',
        icon: AiOutlineControl,
      },
      {
        name: 'Media library(Deprec)',
        icon: AiOutlineProduct,
        path: '/category',
      },
    ],
  },
  {
    name: 'Tools',
    icon: AiOutlineTool,
    path: '/expandable-3',
    children: [
      {
        name: 'File Processor',
        path: '/fileprocessor',
        icon: AiOutlineFileSync,
      },
      {
        name: 'Downloader',
        path: '/downloader',
        icon: AiOutlineDownload,
      },
      {
        name: 'File Mover',
        path: '/filemover',
        icon: AiOutlineFileSync,
      },
      // {
      //   name: 'Other tools',
      //   path: '/tools',
      //   icon: AiOutlineTool,
      // },
      // {
      //   name: 'Migration',
      //   path: '/migration',
      //   icon: AiOutlineTruck,
      // },
    ],
  },
  {
    name: 'Experimental',
    path: '/expandable-4',
    icon: AiOutlineExperiment,
    children: [
      {
        name: 'Post parser',
        path: '/postparser',
        icon: MdOutlineTopic,
      },
      {
        name: '3rd Party integration',
        path: '/3rdpartyintegration',
        icon: MdOutlineIntegrationInstructions,
      },
    ],
  },
  {
    name: 'System',
    icon: AiOutlineSetting,
    path: '/expandable-5',
    children: [
      {
        name: 'Configuration',
        path: '/configuration',
        icon: AiOutlineAppstore,
      },
      {
        name: 'Background Task',
        path: '/backgroundtask',
        icon: AiOutlineInteraction,
      },
      {
        name: 'Log',
        path: '/log',
        icon: AiOutlineFileText,
      },
    ],
  },
  ...(process.env.ICE_CORE_MODE == 'development' ? [{
    name: 'Test',
    path: '/expandable-6',
    icon: undefined,
    children: [
      {
        name: 'common',
        path: '/test',
        icon: AiOutlineCodepenCircle,
      },
      {
        name: 'bakaui',
        path: '/test/bakaui',
        icon: AiOutlineSketch,
      },
      {
        name: 'nextui',
        path: '/test/nextui',
        icon: AiOutlineSketch,
      },
    ],
  }] : []),
];

export { headerMenuConfig, asideMenuConfig };
