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
  AiOutlineSetting,
  AiOutlineDatabase,
  AiOutlineHdd,
  AiOutlineCode,
  AiOutlineDownload,
  AiOutlineSwap,
  AiOutlineEdit,
  AiOutlineBug,
  AiOutlineTags,
  AiOutlineFilter,
  AiOutlineDiff,
  AiOutlineRobot,
  AiOutlineBarChart,
  AiOutlineAudit,
  AiOutlineCloudServer,
} from "react-icons/ai";
import { lazy } from "react";
import { MdOutlineArticle, MdVideoLibrary } from "react-icons/md";
import { TbToolsKitchen } from "react-icons/tb";

import DashboardPage from "@/pages/dashboard";
import ResourcePage from "@/pages/resource";
import LegacyMediaLibraryPage from "@/pages/deprecated/media-library";
import MediaLibraryPage from "@/pages/media-library";
import MediaLibraryTemplatePage from "@/pages/deprecated/media-library-template";
import CustomPropertyPage from "@/pages/custom-property";
import ExtensionGroup from "@/pages/extension-group";
import BulkModification2Page from "@/pages/bulk-modification";
import CachePage from "@/pages/cache";
import AliasPage from "@/pages/alias";
import TextPage from "@/pages/text";
import PlayHistoryPage from "@/pages/play-history";
import SynchronizationOptionsPage from "@/pages/deprecated/synchronization-options";
import Configuration from "@/pages/configuration";
import ThirdPartyConfiguration from "@/pages/third-party-configuration";
import BackgroundTaskPage from "@/pages/background-task";
import Log from "@/pages/log";
import FileProcessorPage from "@/pages/file-processor";
import DownloaderPage from "@/pages/downloader";
import FileMoverPage from "@/pages/file-mover";
import FileNameModifier from "@/pages/file-name-modifier";
import ThirdPartyIntegrationPage from "@/pages/third-party-integration";
import PostParserPage from "@/pages/post-parser";
import ResourceProfilePage from "@/pages/resource-profile";
import PathRuleConfigPage from "@/pages/path-mark-config";
import PathMarksPage from "@/pages/path-marks";
import ProfilerPage from "@/pages/profiler";
import ComparisonPage from "@/pages/comparison";
import AiConfigurationPage from "@/pages/ai-configuration";
import AiUsagePage from "@/pages/ai-usage";
import AiAuditLogPage from "@/pages/ai-audit-log";
import AiCachePage from "@/pages/ai-cache";
import SteamAppsPage from "@/pages/steam-apps";
import DLsiteWorksPage from "@/pages/dlsite-works";
import ExHentaiGalleriesPage from "@/pages/exhentai-galleries";
import { SteamIcon, DLsiteIcon, ExHentaiIcon } from "@/components/SourceIcons";

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
    name: "menu.dashboard",
    path: "/",
    component: DashboardPage,
    icon: AiOutlineDashboard,
    layout: "basic",
    menu: false, // 首页不在菜单中
  },
  {
    name: "menu.resource",
    path: "/resource",
    component: ResourcePage,
    icon: AiOutlinePicture,
    layout: "basic",
    menu: true,
  },
  {
    name: "menu.mediaLibrary",
    icon: MdVideoLibrary,
    menu: true,
    children: [
      {
        name: "menu.mediaLibrary.overview",
        path: "/media-library",
        component: MediaLibraryPage,
        icon: MdVideoLibrary,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.mediaLibrary.setup",
        path: "/path-mark-config",
        component: PathRuleConfigPage,
        icon: AiOutlineControl,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.mediaLibrary.paths",
        path: "/path-marks",
        component: PathMarksPage,
        icon: AiOutlineTags,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.resourceProfile",
        path: "/resource-profile",
        component: ResourceProfilePage,
        icon: AiOutlineFilter,
        layout: "basic",
        menu: true,
        isBeta: false,
      },
    ],
  },
  {
    name: "menu.data",
    icon: AiOutlineDatabase,
    menu: true,
    children: [
      {
        name: "menu.customProperty",
        path: "/customproperty",
        component: CustomPropertyPage,
        icon: AiOutlineRadarChart,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.extensionGroup",
        path: "/extension-group",
        component: ExtensionGroup,
        icon: AiOutlineUngroup,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.cache",
        path: "/cache",
        component: CachePage,
        icon: AiOutlineHdd,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.specialText",
        path: "/text",
        component: TextPage,
        icon: AiOutlineFieldString,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.bulkModification",
        path: "/bulk-modification",
        component: BulkModification2Page,
        icon: AiOutlineForm,
        layout: "basic",
        menu: true,
        isBeta: true,
      },
      {
        name: "menu.comparison",
        path: "/comparison",
        component: ComparisonPage,
        icon: AiOutlineDiff,
        layout: "basic",
        menu: true,
        isBeta: true,
      },
      {
        name: "menu.alias",
        path: "/alias",
        component: AliasPage,
        icon: AiOutlineBranches,
        layout: "basic",
        menu: true,
      }
    ],
  },
  {
    name: "menu.otherPlatforms",
    icon: AiOutlineCloudServer,
    menu: true,
    isBeta: true,
    children: [
      {
        name: "menu.steam",
        path: "/steam-apps",
        component: SteamAppsPage,
        icon: SteamIcon,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.dlsite",
        path: "/dlsite-works",
        component: DLsiteWorksPage,
        icon: DLsiteIcon,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.exhentai",
        path: "/exhentai-galleries",
        component: ExHentaiGalleriesPage,
        icon: ExHentaiIcon,
        layout: "basic",
        menu: true,
      },
    ],
  },
  {
    name: "menu.tools",
    icon: AiOutlineTool,
    menu: true,
    children: [
      {
        name: "menu.fileProcessor",
        path: "/file-processor",
        component: FileProcessorPage,
        icon: AiOutlineCode,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.downloader",
        path: "/downloader",
        component: DownloaderPage,
        icon: AiOutlineDownload,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.fileMover",
        path: "/file-mover",
        component: FileMoverPage,
        icon: AiOutlineSwap,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.fileNameModifier",
        path: "/file-name-modifier",
        component: FileNameModifier,
        icon: AiOutlineEdit,
        layout: "basic",
        isBeta: true,
        menu: true,
      },
      {
        name: "menu.postParser",
        path: "/post-parser",
        component: PostParserPage,
        icon: MdOutlineArticle,
        layout: "basic",
        menu: true,
      },
    ],
  },
  {
    name: "menu.ai",
    icon: AiOutlineRobot,
    menu: true,
    isBeta: true,
    children: [
      {
        name: "menu.ai.configuration",
        path: "/ai/configuration",
        component: AiConfigurationPage,
        icon: AiOutlineSetting,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.ai.usage",
        path: "/ai/usage",
        component: AiUsagePage,
        icon: AiOutlineBarChart,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.ai.auditLog",
        path: "/ai/audit-log",
        component: AiAuditLogPage,
        icon: AiOutlineAudit,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.ai.cache",
        path: "/ai/cache",
        component: AiCachePage,
        icon: AiOutlineHdd,
        layout: "basic",
        menu: true,
      },
    ],
  },
  {
    name: "menu.system",
    icon: AiOutlineSetting,
    menu: true,
    children: [
      {
        name: "menu.configuration",
        path: "/configuration",
        component: Configuration,
        icon: AiOutlineAppstore,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.thirdParty",
        path: "/third-party-configuration",
        component: ThirdPartyConfiguration,
        icon: TbToolsKitchen,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.thirdPartyIntegration",
        path: "/third-party-integration",
        component: ThirdPartyIntegrationPage,
        icon: AiOutlineAppstoreAdd,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.backgroundTask",
        path: "/background-task",
        component: BackgroundTaskPage,
        icon: AiOutlineInteraction,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.performanceProfiler",
        path: "/profiler",
        component: ProfilerPage,
        icon: AiOutlineRadarChart,
        layout: "basic",
        menu: true,
      },
      {
        name: "menu.log",
        path: "/log",
        component: Log,
        icon: AiOutlineFileText,
        layout: "basic",
        menu: true,
      },
    ],
  },
  {
    name: "menu.test",
    path: "/test",
    component: Test,
    icon: AiOutlineBug,
    layout: "basic",
    menu: process.env.NODE_ENV === "development",
  },
];
