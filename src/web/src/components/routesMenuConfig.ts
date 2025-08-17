import {
  AiOutlineDashboard,
  AiOutlinePicture,
  AiOutlineProduct,
  AiOutlineAppstoreAdd,
  AiOutlineControl,
  AiOutlineRadarChart,
  AiOutlineUngroup,
  AiOutlineForm,
  AiOutlineBranches,
  AiOutlineFieldString,
  AiOutlineHistory,
  AiOutlineSync,
  AiOutlineAppstore,
  AiOutlineInteraction,
  AiOutlineFileText,
  AiOutlineTool,
  AiOutlineExperiment,
  AiOutlineSetting,
  AiOutlineDatabase,
  AiOutlineHdd,
  AiOutlineCode,
  AiOutlineDownload,
  AiOutlineSwap,
  AiOutlineEdit,
  AiOutlineBug,
} from "react-icons/ai";
import { lazy } from "react";
import { MdOutlineArticle } from "react-icons/md";
import { TbToolsKitchen } from "react-icons/tb";

import DashboardPage from "@/pages/dashboard";
import ResourcePage from "@/pages/resource";
import MediaLibraryPage from "@/pages/media-library";
import MediaLibraryTemplatePage from "@/pages/media-library-template";
import CustomComponentPage from "@/pages/custom-component";
import CustomPropertyPage from "@/pages/custom-property";
import ExtensionGroup from "@/pages/extension-group";
import BulkModification2Page from "@/pages/bulk-modification2";
import CachePage from "@/pages/cache";
import AliasPage from "@/pages/alias";
import TextPage from "@/pages/text";
import PlayHistoryPage from "@/pages/play-history";
import SynchronizationOptionsPage from "@/pages/synchronization-options";
import Configuration from "@/pages/configuration";
import ThirdPartyConfiguration from "@/pages/third-party-configuration";
import BackgroundTaskPage from "@/pages/background-task";
import Log from "@/pages/log";
import CategoryPage from "@/pages/category";
import FileProcessorPage from "@/pages/file-processor";
import DownloaderPage from "@/pages/downloader";
import FileMoverPage from "@/pages/file-mover";
import FileNameModifier from "@/pages/file-name-modifier";
import ThirdPartyIntegrationPage from "@/pages/third-party-integration";
import PostParserPage from "@/pages/post-parser";

// Lazy load test page to avoid circular dependency
const Test = lazy(() => import("@/pages/test"));

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
    component: DashboardPage,
    icon: AiOutlineDashboard,
    layout: "basic",
    menu: false, // 首页不在菜单中
  },
  {
    name: "Resource",
    path: "/resource",
    component: ResourcePage,
    icon: AiOutlinePicture,
    layout: "basic",
    menu: true,
  },
  {
    name: "Media library",
    path: "/media-library",
    component: MediaLibraryPage,
    icon: AiOutlineProduct,
    layout: "basic",
    menu: true,
  },
  {
    name: "Media library template",
    path: "/media-library-template",
    component: MediaLibraryTemplatePage,
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
        component: SynchronizationOptionsPage,
        icon: AiOutlineSync,
        layout: "basic",
        menu: true,
      },
      {
        name: "Custom property",
        path: "/customproperty",
        component: CustomPropertyPage,
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
        name: "Cache",
        path: "/cache",
        component: CachePage,
        icon: AiOutlineHdd,
        layout: "basic",
        menu: true,
      },
      {
        name: "Alias",
        path: "/alias",
        component: AliasPage,
        icon: AiOutlineBranches,
        layout: "basic",
        menu: true,
      },
      {
        name: "Text",
        path: "/text",
        component: TextPage,
        icon: AiOutlineFieldString,
        layout: "basic",
        menu: true,
      },
      {
        name: "Play history",
        path: "/play-history",
        component: PlayHistoryPage,
        icon: AiOutlineHistory,
        layout: "basic",
        menu: true,
      },
      {
        name: "Bulk modification",
        path: "/bulk-modification2",
        component: BulkModification2Page,
        icon: AiOutlineForm,
        layout: "basic",
        menu: true,
        isBeta: true,
      },
      {
        name: "Custom component",
        path: "/customcomponent",
        isDeprecated: true,
        component: CustomComponentPage,
        icon: AiOutlineControl,
        layout: "basic",
        menu: true,
      },
      {
        name: "Media library",
        path: "/category",
        isDeprecated: true,
        component: CategoryPage,
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
        component: FileProcessorPage,
        icon: AiOutlineCode,
        layout: "basic",
        menu: true,
      },
      {
        name: "Downloader",
        path: "/downloader",
        component: DownloaderPage,
        icon: AiOutlineDownload,
        layout: "basic",
        menu: true,
      },
      {
        name: "File Mover",
        path: "/file-mover",
        component: FileMoverPage,
        icon: AiOutlineSwap,
        layout: "basic",
        menu: true,
      },
      {
        name: "File name modifier",
        path: "/file-name-modifier",
        component: FileNameModifier,
        icon: AiOutlineEdit,
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
    name: "Experimental",
    icon: AiOutlineExperiment,
    menu: true,
    children: [
      {
        name: "Third party integration",
        path: "/third-party-integration",
        component: ThirdPartyIntegrationPage,
        icon: TbToolsKitchen,
        layout: "basic",
        menu: true,
      },
      {
        name: "Post parser",
        path: "/post-parser",
        component: PostParserPage,
        icon: MdOutlineArticle,
        layout: "basic",
        menu: true,
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
        name: "Third party",
        path: "/third-party-configuration",
        component: ThirdPartyConfiguration,
        icon: TbToolsKitchen,
        layout: "basic",
        menu: true,
      },
      {
        name: "Background Task",
        path: "/background-task",
        component: BackgroundTaskPage,
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
  {
    name: "Test",
    path: "/test",
    component: Test,
    icon: AiOutlineBug,
    layout: "basic",
    menu: process.env.NODE_ENV === "development",
  },
];
