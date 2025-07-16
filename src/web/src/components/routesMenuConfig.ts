import { AiOutlineDashboard, AiOutlinePicture, AiOutlineProduct, AiOutlineAppstoreAdd, AiOutlineControl, AiOutlineRadarChart, AiOutlineUngroup, AiOutlineForm, AiOutlineDashboard as AiOutlineDashboard2, AiOutlineBranches, AiOutlineFieldString, AiOutlineHistory, AiOutlineSync, AiOutlineAppstore, AiOutlineInteraction, AiOutlineFileText, AiOutlineTool, AiOutlineExperiment, AiOutlineSetting, AiOutlineDatabase } from "react-icons/ai";
import Dashboard from "@/pages/dashboard";
import Resource from "@/pages/resource";
import MediaLibrary from "@/pages/media-library";
import MediaLibraryTemplate from "@/pages/media-library-template";
import CustomComponent from "@/pages/custom-component";
import CustomProperty from "@/pages/custom-property";
import ExtensionGroup from "@/pages/extension-group";
import BulkModification2 from "@/pages/bulk-modification2";
import Cache from "@/pages/cache";
import Alias from "@/pages/alias";
import Text from "@/pages/text";
import PlayHistory from "@/pages/play-history";
import SynchronizationOptions from "@/pages/synchronization-options";
import Configuration from "@/pages/configuration";
import BackgroundTask from "@/pages/background-task";
import Log from "@/pages/log";
import Category from "@/pages/category";
import FileProcessor from "@/pages/file-processor";
import Downloader from "@/pages/downloader";
import FileMover from "@/pages/file-mover";
import FileNameModifier from "@/pages/file-name-modifier";


export interface RouteMenuItem {
  name: string;
  path?: string;
  component?: React.ComponentType<any>;
  icon?: any;
  layout?: "basic" | "blank";
  children?: RouteMenuItem[];
  isBeta?: boolean;
  isDeprecated?: boolean;
  menu?: boolean;
}

export const routesMenuConfig: RouteMenuItem[] = [
  {
    name: "Dashboard",
    path: "/",
    component: Dashboard,
    icon: AiOutlineDashboard,
    layout: "basic",
    menu: false, // 首页不在菜单中
  },
  {
    name: "Resource",
    path: "/resource",
    component: Resource,
    icon: AiOutlinePicture,
    layout: "basic",
    menu: true,
  },
  {
    name: "Media library",
    path: "/media-library",
    component: MediaLibrary,
    icon: AiOutlineProduct,
    layout: "basic",
    menu: true,
  },
  {
    name: "Media library template",
    path: "/media-library-template",
    component: MediaLibraryTemplate,
    icon: AiOutlineAppstoreAdd,
    layout: "basic",
    menu: true, 
  },
  {
    name: "Data",
    icon: AiOutlineDatabase,
    menu: true,
    children: [
      {
        name: "Synchronization options",
        path: "/synchronization-options",
        component: SynchronizationOptions,
        icon: AiOutlineSync,
        layout: "basic",
        menu: true,
      },
      {
        name: "Custom property",
        path: "/customproperty",
        component: CustomProperty,
        icon: AiOutlineRadarChart,
        layout: "basic",
        menu: true,
      },
      {
        name: "Extension group",
        path: "/extension-group",
        component: ExtensionGroup,
        icon: AiOutlineUngroup,
        layout: "basic",
        menu: true,
      },
      {
        name: "Bulk modification",
        path: "/bulk-modification2",
        component: BulkModification2,
        icon: AiOutlineForm,
        layout: "basic",
        menu: true,
      },
      {
        name: "Cache",
        path: "/cache",
        component: Cache,
        icon: AiOutlineDashboard2,
        layout: "basic",
        menu: true,
      },
      {
        name: "Alias",
        path: "/alias",
        component: Alias,
        icon: AiOutlineBranches,
        layout: "basic",
        menu: true,
      },
      {
        name: "Text",
        path: "/text",
        component: Text,
        icon: AiOutlineFieldString,
        layout: "basic",
        menu: true,
      },
      {
        name: "Play history",
        path: "/play-history",
        component: PlayHistory,
        icon: AiOutlineHistory,
        layout: "basic",
        menu: true,
      },
      {
        name: "Custom component",
        path: "/customcomponent",
        isDeprecated: true,
        component: CustomComponent,
        icon: AiOutlineControl,
        layout: "basic",
        menu: true,
      },
      {
        name: "Media library",
        path: "/category",
        isDeprecated: true,
        component: Category, 
        icon: AiOutlineProduct,
        layout: "basic",
        menu: true,
      },
    ],
  },
  {
    name: "Tools",
    icon: AiOutlineTool,
    menu: true,
    children: [
      {
        name: "File Processor",
        path: "/file-processor",
        component: FileProcessor,
        icon: AiOutlineDashboard2,
        layout: "basic",
        menu: true,
      },
      {
        name: "Downloader",
        path: "/downloader",
        component: Downloader,
        icon: AiOutlineDashboard2,
        layout: "basic",
        menu: true,
      },
      {
        name: "File Mover",
        path: "/file-mover",
        component: FileMover,
        icon: AiOutlineDashboard2,
        layout: "basic",
        menu: true,
      },
      {
        name: "File name modifier",
        path: "/file-name-modifier",
        component: FileNameModifier,
        icon: AiOutlineDashboard2,
        layout: "basic",
        isBeta: true,
        menu: true,
      },
      {
        name: "Migration",
        path: "/migration",
        // component: Migration,
        icon: AiOutlineExperiment,
        layout: "basic",
        menu: false, // 不在菜单中
      },
    ],
  },
  {
    name: "System",
    icon: AiOutlineSetting,
    menu: true,
    children: [
      {
        name: "Configuration",
        path: "/configuration",
        component: Configuration,
        icon: AiOutlineAppstore,
        layout: "basic",
        menu: true,
      },
      {
        name: "Background Task",
        path: "/background-task",
        component: BackgroundTask,
        icon: AiOutlineInteraction,
        layout: "basic",
        menu: true,
      },
      {
        name: "Log",
        path: "/log",
        component: Log,
        icon: AiOutlineFileText,
        layout: "basic",
        menu: true,
      },
    ],
  },
]; 