import i18n from "i18next";
import { initReactI18next } from "react-i18next";

// Modular imports - English
import enCommon from "@/locales/en/common.json";
import enValidation from "@/locales/en/validation.json";
import enErrors from "@/locales/en/errors.json";
import enStatus from "@/locales/en/status.json";
import enDatetime from "@/locales/en/datetime.json";
import enEnums from "@/locales/en/enums.json";

// English - Pages
import enDashboard from "@/locales/en/pages/dashboard.json";
import enResource from "@/locales/en/pages/resource.json";
import enConfiguration from "@/locales/en/pages/configuration.json";
import enDownloader from "@/locales/en/pages/downloader.json";
import enFileProcessor from "@/locales/en/pages/fileProcessor.json";
import enMediaLibrary from "@/locales/en/pages/mediaLibrary.json";
import enPathMark from "@/locales/en/pages/pathMark.json";
import enResourceProfile from "@/locales/en/pages/resourceProfile.json";
import enBulkModification from "@/locales/en/pages/bulkModification.json";
import enAlias from "@/locales/en/pages/alias.json";
import enCustomProperty from "@/locales/en/pages/customProperty.json";
import enBackgroundTask from "@/locales/en/pages/backgroundTask.json";
import enCache from "@/locales/en/pages/cache.json";
import enFileNameModifier from "@/locales/en/pages/fileNameModifier.json";
import enFileMover from "@/locales/en/pages/fileMover.json";
import enLog from "@/locales/en/pages/log.json";
import enPlayHistory from "@/locales/en/pages/playHistory.json";
import enWelcome from "@/locales/en/pages/welcome.json";
import enThirdParty from "@/locales/en/pages/thirdParty.json";
import enThirdPartyIntegration from "@/locales/en/pages/thirdPartyIntegration.json";
import enThirdPartyConfiguration from "@/locales/en/pages/thirdPartyConfiguration.json";
import enExtensionGroup from "@/locales/en/pages/extensionGroup.json";
import enText from "@/locales/en/pages/text.json";
import enPostParser from "@/locales/en/pages/postParser.json";
import enPathMarks from "@/locales/en/pages/pathMarks.json";
import enPathMarkConfig from "@/locales/en/pages/pathMarkConfig.json";

// English - Components
import enFileExplorer from "@/locales/en/components/fileExplorer.json";
import enResourceFilter from "@/locales/en/components/resourceFilter.json";
import enEnhancer from "@/locales/en/components/enhancer.json";
import enProperty from "@/locales/en/components/property.json";
import enMediaPlayer from "@/locales/en/components/mediaPlayer.json";
import enOnboarding from "@/locales/en/components/onboarding.json";
import enResourceComponent from "@/locales/en/components/resource.json";
import enFloatingAssistant from "@/locales/en/components/floatingAssistant.json";

// New modular imports - Chinese
import cnCommon from "@/locales/cn/common.json";
import cnValidation from "@/locales/cn/validation.json";
import cnErrors from "@/locales/cn/errors.json";
import cnStatus from "@/locales/cn/status.json";
import cnDatetime from "@/locales/cn/datetime.json";
import cnEnums from "@/locales/cn/enums.json";

// Chinese - Pages
import cnDashboard from "@/locales/cn/pages/dashboard.json";
import cnResource from "@/locales/cn/pages/resource.json";
import cnConfiguration from "@/locales/cn/pages/configuration.json";
import cnDownloader from "@/locales/cn/pages/downloader.json";
import cnFileProcessor from "@/locales/cn/pages/fileProcessor.json";
import cnMediaLibrary from "@/locales/cn/pages/mediaLibrary.json";
import cnPathMark from "@/locales/cn/pages/pathMark.json";
import cnResourceProfile from "@/locales/cn/pages/resourceProfile.json";
import cnBulkModification from "@/locales/cn/pages/bulkModification.json";
import cnAlias from "@/locales/cn/pages/alias.json";
import cnCustomProperty from "@/locales/cn/pages/customProperty.json";
import cnBackgroundTask from "@/locales/cn/pages/backgroundTask.json";
import cnCache from "@/locales/cn/pages/cache.json";
import cnFileNameModifier from "@/locales/cn/pages/fileNameModifier.json";
import cnFileMover from "@/locales/cn/pages/fileMover.json";
import cnLog from "@/locales/cn/pages/log.json";
import cnPlayHistory from "@/locales/cn/pages/playHistory.json";
import cnWelcome from "@/locales/cn/pages/welcome.json";
import cnThirdParty from "@/locales/cn/pages/thirdParty.json";
import cnThirdPartyIntegration from "@/locales/cn/pages/thirdPartyIntegration.json";
import cnThirdPartyConfiguration from "@/locales/cn/pages/thirdPartyConfiguration.json";
import cnExtensionGroup from "@/locales/cn/pages/extensionGroup.json";
import cnText from "@/locales/cn/pages/text.json";
import cnPostParser from "@/locales/cn/pages/postParser.json";
import cnPathMarks from "@/locales/cn/pages/pathMarks.json";
import cnPathMarkConfig from "@/locales/cn/pages/pathMarkConfig.json";

// Chinese - Components
import cnFileExplorer from "@/locales/cn/components/fileExplorer.json";
import cnResourceFilter from "@/locales/cn/components/resourceFilter.json";
import cnEnhancer from "@/locales/cn/components/enhancer.json";
import cnProperty from "@/locales/cn/components/property.json";
import cnMediaPlayer from "@/locales/cn/components/mediaPlayer.json";
import cnOnboarding from "@/locales/cn/components/onboarding.json";
import cnResourceComponent from "@/locales/cn/components/resource.json";
import cnFloatingAssistant from "@/locales/cn/components/floatingAssistant.json";

// Merge all English resources
const enResources = {
  ...enCommon,
  ...enValidation,
  ...enErrors,
  ...enStatus,
  ...enDatetime,
  ...enEnums,
  // Pages
  ...enDashboard,
  ...enResource,
  ...enConfiguration,
  ...enDownloader,
  ...enFileProcessor,
  ...enMediaLibrary,
  ...enPathMark,
  ...enResourceProfile,
  ...enBulkModification,
  ...enAlias,
  ...enCustomProperty,
  ...enBackgroundTask,
  ...enCache,
  ...enFileNameModifier,
  ...enFileMover,
  ...enLog,
  ...enPlayHistory,
  ...enWelcome,
  ...enThirdParty,
  ...enThirdPartyIntegration,
  ...enThirdPartyConfiguration,
  ...enExtensionGroup,
  ...enText,
  ...enPostParser,
  ...enPathMarks,
  ...enPathMarkConfig,
  // Components
  ...enFileExplorer,
  ...enResourceFilter,
  ...enEnhancer,
  ...enProperty,
  ...enMediaPlayer,
  ...enOnboarding,
  ...enResourceComponent,
  ...enFloatingAssistant,
};

// Merge all Chinese resources
const cnResources = {
  ...cnCommon,
  ...cnValidation,
  ...cnErrors,
  ...cnStatus,
  ...cnDatetime,
  ...cnEnums,
  // Pages
  ...cnDashboard,
  ...cnResource,
  ...cnConfiguration,
  ...cnDownloader,
  ...cnFileProcessor,
  ...cnMediaLibrary,
  ...cnPathMark,
  ...cnResourceProfile,
  ...cnBulkModification,
  ...cnAlias,
  ...cnCustomProperty,
  ...cnBackgroundTask,
  ...cnCache,
  ...cnFileNameModifier,
  ...cnFileMover,
  ...cnLog,
  ...cnPlayHistory,
  ...cnWelcome,
  ...cnThirdParty,
  ...cnThirdPartyIntegration,
  ...cnThirdPartyConfiguration,
  ...cnExtensionGroup,
  ...cnText,
  ...cnPostParser,
  ...cnPathMarks,
  ...cnPathMarkConfig,
  // Components
  ...cnFileExplorer,
  ...cnResourceFilter,
  ...cnEnhancer,
  ...cnProperty,
  ...cnMediaPlayer,
  ...cnOnboarding,
  ...cnResourceComponent,
  ...cnFloatingAssistant,
};

// 只初始化一次，防止热更新或多次 import 时重复初始化
if (!i18n.isInitialized) {
  i18n.use(initReactI18next).init({
    resources: {
      cn: { translation: cnResources },
      en: { translation: enResources },
    },
    lng: "en", // 默认语言
    fallbackLng: "en",
    returnNull: false,
    interpolation: { escapeValue: false },
    parseMissingKeyHandler: (key: string) => key,
    returnObjects: false,
  });
  console.log("i18n initialized");
}

// 开发模式下支持 i18n 资源热更新
// 使用 import.meta.glob 动态监听所有 locale 文件的变化
if (import.meta.hot) {
  // 动态导入所有英文翻译文件
  const enModules = import.meta.glob("./locales/en/**/*.json");
  Object.keys(enModules).forEach((modulePath) => {
    import.meta.hot?.accept(modulePath, (newModule) => {
      if (newModule) {
        i18n.addResourceBundle("en", "translation", newModule.default, true, true);
        console.log(`i18n hot updated: ${modulePath}`);
      }
    });
  });

  // 动态导入所有中文翻译文件
  const cnModules = import.meta.glob("./locales/cn/**/*.json");
  Object.keys(cnModules).forEach((modulePath) => {
    import.meta.hot?.accept(modulePath, (newModule) => {
      if (newModule) {
        i18n.addResourceBundle("cn", "translation", newModule.default, true, true);
        console.log(`i18n hot updated: ${modulePath}`);
      }
    });
  });
}

/**
 * Helper function to convert PascalCase to camelCase
 * @param str - PascalCase string
 * @returns camelCase string
 */
function pascalToCamel(str: string): string {
  return str.charAt(0).toLowerCase() + str.slice(1);
}

/**
 * Get enum translation key by converting old format to new format
 * @param enumType - The enum type (e.g., 'PropertyType', 'ResourceTag', 'Combinator')
 * @param label - The label from constants (PascalCase)
 * @returns The new i18n key
 */
export function getEnumKey(enumType: string, label: string): string {
  // Convert enum type from PascalCase to camelCase
  const camelEnumType = pascalToCamel(enumType);
  // Convert label from PascalCase to camelCase
  const camelLabel = pascalToCamel(label);

  return `enum.${camelEnumType}.${camelLabel}`;
}

export default i18n;
