import i18n from "i18next";
import { initReactI18next } from "react-i18next";

import cnResources from "@/locales/cn.json";
import enResources from "@/locales/en.json";

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
if (import.meta.hot) {
  import.meta.hot.accept("@/locales/cn.json", (newModule) => {
    if (newModule) {
      i18n.addResourceBundle("cn", "translation", newModule.default, true, true);
      console.log("i18n cn resources hot updated");
    }
  });
  import.meta.hot.accept("@/locales/en.json", (newModule) => {
    if (newModule) {
      i18n.addResourceBundle("en", "translation", newModule.default, true, true);
      console.log("i18n en resources hot updated");
    }
  });
}

export default i18n;
